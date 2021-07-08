using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SOARIntegration.Xero.Api.Accounts.Repository;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xero.Api.Core;
using Xero.Api.Core.Model;
using static SOAR.Shared.Xero.Common.Model.Mode;

namespace Xero.Api.Accounts
{
	public class ImportService
	{
		#region Local Variables
		private readonly ILogger c_log;
		private readonly Context c_context;
		private IXeroCoreApi c_api;
		private IConfigurationRoot c_config;
		private SyncMode c_syncMode;

		#endregion

		#region Constructor
		public ImportService(ILogger<ImportService> log, Context context, String orgKey, IConfigurationRoot config, string syncMode)
		{
			c_log = log;
			c_context = context;
			c_api = SOARIntegration.Xero.Common.Helpers.Application.Initialise(orgKey);
			c_context.Database.SetCommandTimeout(3600);
			c_config = config;
			c_syncMode = syncMode == "-f" ? SyncMode.Full : SyncMode.Delta;
		}
		#endregion

		#region Public Methods
		public void Import()
		{
			string orgName = c_config.GetValue<string>("XeroApi:Org");
			Task.Run(() => this.ImportManualJournals(orgName)).Wait();
			Task.Run(this.ImportLinkedTransactions).Wait();
			Task.Run(() => this.ImportJournals(orgName)).Wait();
			Task.Run(() => this.ImportItems(orgName)).Wait();
			Task.Run(() => this.ImportInvoices(orgName)).Wait();
			Task.Run(() => this.ImportPayments(orgName)).Wait();
			//Task.Run(this.ImportReceipts).Wait(): deprecated, the data is now part of invoices;
		}
		#endregion

		#region Private Methods
		private async Task ImportManualJournals(string org)
		{
			IEnumerable<ManualJournal> manualJournals = new List<ManualJournal>();
			int windowPeriodInYears = c_config.GetValue<int>("XeroApi:WindowPeriodInYears");
			int deltaWindowPeriodInDays = c_config.GetValue<int>("XeroApi:DeltaWindowPeriodInDays");
			DateTime windowStartDate = c_syncMode == SyncMode.Full ? DateTime.Now.AddYears(-1 * windowPeriodInYears) : DateTime.Now.AddDays(deltaWindowPeriodInDays);
			int page = 1;

			c_log.LogInformation($"Fetching manual journals page ({page})...");
			var manualJournalsTemp = await c_api.ManualJournals
				.ModifiedSince(windowStartDate)
				.Page(page)
				.FindAsync();

			while (manualJournalsTemp.Count() > 0)
			{
				manualJournals = manualJournals.Concat(manualJournalsTemp);
				page++;
				c_log.LogInformation($"Fetching manual journals page ({page})...");
				manualJournalsTemp = await c_api.ManualJournals
				   .ModifiedSince(windowStartDate)
				   .Page(page)
				   .FindAsync();
				await Task.Delay(2000);
			}

			var existing = await c_context.ManualJournals.Where(x => x.OrgName == org).ToArrayAsync();
			var existingIds = c_syncMode == SyncMode.Full ? existing.Select(e => e.Id).ToArray() : existing.Select(e => e.Id).Intersect(manualJournals.Select(x => x.Id)).ToArray();
			var existingLines = await c_context.Lines.Where(l => existingIds.Contains(l.ReferenceId)).ToArrayAsync();

			if (existingIds.Any())
			{
				c_context.ManualJournals.RemoveRange(existing.Where(l => existingIds.Contains(l.Id)));
				c_context.Lines.RemoveRange(existingLines);
				await c_context.SaveChangesAsync();
			}

			var count = 0;
			foreach (var manualJournal in manualJournals)
			{
				c_context.ManualJournals.Add(
					new SOAR.Shared.Xero.Common.Model.ManualJournal()
					{
						Id = manualJournal.Id,
						Date = manualJournal.Date,
						Status = manualJournal.Status?.GetEnumMemberValue(),
						LineAmountTypes = manualJournal.LineAmountTypes?.GetEnumMemberValue(),
						Url = manualJournal.Url,
						ShowOnCashBasisReports = manualJournal.ShowOnCashBasisReports,
						Narration = manualJournal.Narration,
						HasAttachments = manualJournal.HasAttachments,
						OrgName = org
					}
				);

				if (manualJournal.Lines != null)
				{
					foreach (var line in manualJournal.Lines)
					{
						c_context.Lines.Add(this.GetLine(line, manualJournal.Id));
					}
				}

				count++;
			}
			await c_context.SaveChangesAsync();
			c_log.LogInformation($"Imported {count} manual journal(s)");
		}

		private async Task ImportLinkedTransactions()
		{
			var existing = await c_context.LinkedTransactions.ToArrayAsync();
			c_context.LinkedTransactions.RemoveRange(existing);
			await c_context.SaveChangesAsync();

			IEnumerable<LinkedTransaction> linkedTransactions = new List<LinkedTransaction>();
			int page = 1;
			int windowPeriodInYears = c_config.GetValue<int>("XeroApi:WindowPeriodInYears");

			c_log.LogInformation($"Fetching linked transactions page ({page})...");
			var linkedTransactionsTemp = await c_api.LinkedTransactions
				.ModifiedSince(DateTime.Now.AddYears(-1 * windowPeriodInYears))
				.Page(page)
				.FindAsync();

			while (linkedTransactionsTemp.Count() > 0)
			{
				linkedTransactions = linkedTransactions.Concat(linkedTransactionsTemp);
				page++;
				c_log.LogInformation($"Fetching linked transactions page ({page})...");
				linkedTransactionsTemp = await c_api.LinkedTransactions
				   .ModifiedSince(DateTime.Now.AddYears(-1 * windowPeriodInYears))
				   .Page(page)
				   .FindAsync();
				await Task.Delay(2000);
			}

			var count = 0;
			foreach (var linkedTransaction in await c_api.LinkedTransactions.FindAsync())
			{
				c_context.LinkedTransactions.Add(
					new SOAR.Shared.Xero.Common.Model.LinkedTransaction()
					{
						Id = linkedTransaction.Id,
						SourceTransactionID = linkedTransaction.SourceTransactionID,
						SourceLineItemID = linkedTransaction.SourceLineItemID,
						ContactID = linkedTransaction.ContactID,
						TargetTransactionID = linkedTransaction.TargetTransactionID,
						TargetLineItemID = linkedTransaction.TargetLineItemID,
						Status = linkedTransaction.Status.GetEnumMemberValue(),
						Type = linkedTransaction.Type.GetEnumMemberValue()
					}
				);

				count++;
			}
			await c_context.SaveChangesAsync();
			c_log.LogInformation($"Imported {count} linked transaction(s)");
		}

		private async Task ImportJournals(string org)
		{
			int windowPeriodInYears = c_config.GetValue<int>("XeroApi:WindowPeriodInYears");
			int deltaWindowPeriodInDays = c_config.GetValue<int>("XeroApi:DeltaWindowPeriodInDays");
			DateTime windowStartDate = c_syncMode == SyncMode.Full ? DateTime.Now.AddYears(-1 * windowPeriodInYears) : DateTime.Now.AddDays(deltaWindowPeriodInDays);

			var journals = await c_api.Journals
				.ModifiedSince(windowStartDate)
				.FindAsync();

			var existing = await c_context.Journals.Where(x => x.OrgName == org).ToArrayAsync();
			var existingIds = c_syncMode == SyncMode.Full ? existing.Select(e => e.Id).ToArray() : existing.Select(e => e.Id).Intersect(journals.Select(x => x.Id));
			var existingLines = await c_context.Lines.Where(l => existingIds.Contains(l.ReferenceId)).ToArrayAsync();

			c_context.Journals.RemoveRange(existing.Where(x => existingIds.Contains(x.Id)));
			c_context.Lines.RemoveRange(existingLines);

			await c_context.SaveChangesAsync();

			var count = 0;
			foreach (var journal in journals)
			{
				c_context.Journals.Add(
					new SOAR.Shared.Xero.Common.Model.Journal()
					{
						Id = journal.Id,
						Date = journal.Date,
						Number = journal.Number,
						CreatedDateUtc = journal.CreatedDateUtc,
						Reference = journal.Reference,
						SourceId = journal.SourceId,
						SourceType = journal.SourceType?.GetEnumMemberValue(),
						OrgName = org
					}
				);

				if (journal.Lines != null)
				{
					foreach (var line in journal.Lines)
					{
						c_context.Lines.Add(this.GetLine(line, journal.Id));
					}
				}

				count++;
			}
			await c_context.SaveChangesAsync();
			c_log.LogInformation($"Imported {count} journal(s)");
		}

		private async Task ImportItems(string org)
		{
			int windowPeriodInYears = c_config.GetValue<int>("XeroApi:WindowPeriodInYears");
			int deltaWindowPeriodInDays = c_config.GetValue<int>("XeroApi:DeltaWindowPeriodInDays");
			DateTime windowStartDate = c_syncMode == SyncMode.Full ? DateTime.Now.AddYears(-1 * windowPeriodInYears) : DateTime.Now.AddDays(deltaWindowPeriodInDays);

			var items = await c_api.Items
				.ModifiedSince(windowStartDate)
				.FindAsync();

			var existing = await c_context.Items.Where(x => x.OrgName == org).ToArrayAsync();
			var existingIds = c_syncMode == SyncMode.Full ? existing.Select(e => e.Id).ToArray() : existing.Select(e => e.Id).Intersect(items.Select(x => x.Id));
			var existingPurchaseDetails = await c_context.PurchaseDetails.Where(l => existingIds.Contains(l.ItemId)).ToArrayAsync();
			var existingSalesDetails = await c_context.SalesDetails.Where(l => existingIds.Contains(l.ItemId)).ToArrayAsync();

			c_context.Items.RemoveRange(existing.Where(x => existingIds.Contains(x.Id)));
			c_context.PurchaseDetails.RemoveRange(existingPurchaseDetails);
			c_context.SalesDetails.RemoveRange(existingSalesDetails);

			await c_context.SaveChangesAsync();

			var count = 0;
			foreach (var item in items)
			{
				c_context.Items.Add(
					new SOAR.Shared.Xero.Common.Model.Item()
					{
						Id = item.Id,
						Code = item.Code,
						Description = item.Description,
						InventoryAssetAccountCode = item.InventoryAssetAccountCode,
						QuantityOnHand = item.QuantityOnHand,
						IsSold = item.IsSold,
						IsPurchased = item.IsPurchased,
						PurchaseDescription = item.PurchaseDescription,
						IsTrackedAsInventory = item.IsTrackedAsInventory,
						TotalCostPool = item.TotalCostPool,
						Name = item.Name,
						OrgName = org
					}
				);

				if (item.PurchaseDetails != null)
				{
					c_context.PurchaseDetails.Add(
						new SOAR.Shared.Xero.Common.Model.PurchaseDetails()
						{
							ItemId = item.Id,
							UnitPrice = item.PurchaseDetails.UnitPrice,
							AccountCode = item.PurchaseDetails.AccountCode,
							TaxType = item.PurchaseDetails.TaxType,
							COGSAccountCode = item.PurchaseDetails.COGSAccountCode
						}
					);
				}

				if (item.SalesDetails != null)
				{
					c_context.SalesDetails.Add(
						new SOAR.Shared.Xero.Common.Model.SalesDetails()
						{
							ItemId = item.Id,
							UnitPrice = item.SalesDetails.UnitPrice,
							AccountCode = item.SalesDetails.AccountCode,
							TaxType = item.SalesDetails.TaxType
						}
					);
				}

				count++;
			}
			await c_context.SaveChangesAsync();
			c_log.LogInformation($"Imported {count} item(s)");
		}

		private async Task ImportInvoices(string org)
		{
			var count = 0;
			var lineItemCount = 0;
			var overpaymentCount = 0;
			var overpaymentAllocationCount = 0;
			var prepaymentCount = 0;
			var prepaymentAllocationCount = 0;
			var creditNoteCount = 0;
			var creditNoteAllocationCount = 0;

			try
			{
				int windowPeriodInYears = c_config.GetValue<int>("XeroApi:WindowPeriodInYears");
				int deltaWindowPeriodInDays = c_config.GetValue<int>("XeroApi:DeltaWindowPeriodInDays");
				DateTime windowStartDate = c_syncMode == SyncMode.Full ? DateTime.Now.AddYears(-1 * windowPeriodInYears) : DateTime.Now.AddDays(deltaWindowPeriodInDays);

				IEnumerable<Invoice> invoices = new List<Invoice>();
				int page = 1;

				c_log.LogInformation($"Fetching invoices page ({page})...");
				var invoicesTemp = await c_api.Invoices
					.ModifiedSince(windowStartDate)
					.Page(page)
					.FindAsync();
				 
				while (invoicesTemp.Count() > 0)
				{
					invoices = invoices.Concat(invoicesTemp);
					page++;
					c_log.LogInformation($"Fetching invoices page ({page})...");
					invoicesTemp = await c_api.Invoices
						.ModifiedSince(windowStartDate)
						.Page(page)
						.FindAsync();
					await Task.Delay(2000);
				}

				var existing = await c_context.Invoices.Where(x => x.OrgName == org).ToArrayAsync();
				var existingIds = c_syncMode == SyncMode.Full ? existing.Select(x => x.Id) : existing.Select(x => x.Id).Intersect(invoices.Select(x => x.Id));
				var existingOverpayments = await c_context.Overpayments.Where(l => l.ReferenceId != null && existingIds.Contains(l.ReferenceId.Value)).ToArrayAsync();
				var existingOverpaymentIds = existingOverpayments.Select(o => o.Id).ToArray();
				var existingPrepayments = await c_context.Prepayments.Where(l => l.ReferenceId != null && existingIds.Contains(l.ReferenceId.Value)).ToArrayAsync();
				var existingPrepaymentIds = existingPrepayments.Select(o => o.Id).ToArray();
				var existingCreditNotes = await c_context.CreditNotes.Where(l => l.ReferenceId != null && existingIds.Contains(l.ReferenceId)).ToArrayAsync();
				var existingCreditNoteAllocations = await c_context.CreditNoteAllocations.Where(l => l.InvoiceId != null && existingIds.Contains(l.InvoiceId.Value)).ToArrayAsync();

				var existingLineItemReferenceIds = existingIds
					.Concat(existingOverpaymentIds)
					.ToArray();

				var existingLineItems = await c_context.LineItems.Where(l => existingLineItemReferenceIds.Contains(l.ReferenceId)).ToArrayAsync();
				var existingLineItemIds = existingLineItems.Select(l => l.LineItemId);
				var existingInvoiceLineItemTracking = await c_context.InvoiceLineItemTrackingCategories.Where(l => existingLineItemIds.Contains(l.LineItemId)).ToArrayAsync();

				var existingTrackingCategories = await c_context.InvoiceTrackingCategories.Where(x => x.OrgName == org).ToArrayAsync();
				var existingTrackingCategoryIds = await c_context.InvoiceTrackingCategories.Select(l => l.Id).ToArrayAsync();
				var existingTrackingOptions = await c_context.InvoiceTrackingOptions.Where(l => existingTrackingCategoryIds.Contains(l.TrackingCategoryId)).ToArrayAsync();

				var existingOverpaymentAllocations = await c_context.OverpaymentAllocations.Where(o => existingOverpaymentIds.Contains(o.OverpaymentId)).ToArrayAsync();
				var existingPrepaymentAllocations = await c_context.PrepaymentAllocations.Where(o => existingPrepaymentIds.Contains(o.PrepaymentId)).ToArrayAsync();

				c_context.Invoices.RemoveRange(existing.Where(x => existingIds.Contains(x.Id)));
				c_context.Overpayments.RemoveRange(existingOverpayments);
				c_context.OverpaymentAllocations.RemoveRange(existingOverpaymentAllocations);
				c_context.Prepayments.RemoveRange(existingPrepayments);
				c_context.PrepaymentAllocations.RemoveRange(existingPrepaymentAllocations);
				c_context.LineItems.RemoveRange(existingLineItems);
				c_context.InvoiceLineItemTrackingCategories.RemoveRange(existingInvoiceLineItemTracking);
				c_context.InvoiceTrackingCategories.RemoveRange(existingTrackingCategories);
				c_context.InvoiceTrackingOptions.RemoveRange(existingTrackingOptions);
				c_context.CreditNoteAllocations.RemoveRange(existingCreditNoteAllocations);
				c_context.CreditNotes.RemoveRange(existingCreditNotes);

				await c_context.SaveChangesAsync();

				int invoicesCount = invoices.Count();

				List<SOAR.Shared.Xero.Common.Model.Prepayment> prePayments = new List<SOAR.Shared.Xero.Common.Model.Prepayment>();
				List<SOAR.Shared.Xero.Common.Model.CreditNote> creditNotes = new List<SOAR.Shared.Xero.Common.Model.CreditNote>();
				List<SOAR.Shared.Xero.Common.Model.Overpayment> overPayments = new List<SOAR.Shared.Xero.Common.Model.Overpayment>();

				List<SOAR.Shared.Xero.Common.Model.LineItem> creditNoteLineItems = new List<SOAR.Shared.Xero.Common.Model.LineItem>();
				List<SOAR.Shared.Xero.Common.Model.LineItem> overPaymentLineItems = new List<SOAR.Shared.Xero.Common.Model.LineItem>();
				List<SOAR.Shared.Xero.Common.Model.LineItem> prePaymentLineItems = new List<SOAR.Shared.Xero.Common.Model.LineItem>();

				List<SOAR.Shared.Xero.Common.Model.CreditNoteAllocation> creditNoteAllocations = new List<SOAR.Shared.Xero.Common.Model.CreditNoteAllocation>();
				List<SOAR.Shared.Xero.Common.Model.PrepaymentAllocation> prePaymentAllocations = new List<SOAR.Shared.Xero.Common.Model.PrepaymentAllocation>();
				List<SOAR.Shared.Xero.Common.Model.OverpaymentAllocation> overPaymentAllocations = new List<SOAR.Shared.Xero.Common.Model.OverpaymentAllocation>();

				List<SOAR.Shared.Xero.Common.Model.InvoiceTrackingCategory> invoiceTrackingCategories = new List<SOAR.Shared.Xero.Common.Model.InvoiceTrackingCategory>();
				List<SOAR.Shared.Xero.Common.Model.InvoiceTrackingOption> invoiceTrackingOptions = new List<SOAR.Shared.Xero.Common.Model.InvoiceTrackingOption>();

				foreach (var invoice in invoices)
				{
					c_log.LogInformation($"Processing invoice ({count} of {invoicesCount})...");

					var inv = new SOAR.Shared.Xero.Common.Model.Invoice()
					{
						Id = invoice.Id,
						OrgName = org,
						Number = invoice.Number,
						Type = invoice.Type.GetEnumMemberValue(),
						Status = invoice.Status.GetEnumMemberValue(),
						LineAmountTypes = invoice.LineAmountTypes.GetEnumMemberValue(),
						Date = invoice.Date,
						DueDate = invoice.DueDate,
						ExpectedPaymentDate = invoice.ExpectedPaymentDate,
						PlannedPaymentDate = invoice.PlannedPaymentDate,
						SubTotal = invoice.SubTotal,
						TotalTax = invoice.TotalTax,
						Total = invoice.Total,
						TotalDiscount = invoice.TotalDiscount,
						CurrencyCode = invoice.CurrencyCode,
						CurrencyRate = invoice.CurrencyRate,
						FullyPaidOnDate = invoice.FullyPaidOnDate,
						AmountDue = invoice.AmountDue,
						AmountPaid = invoice.AmountPaid,
						AmountCredited = invoice.AmountCredited,
						HasAttachments = invoice.HasAttachments,
						BrandingThemeId = invoice.BrandingThemeId,
						Url = invoice.Url,
						Reference = invoice.Reference,
						SentToContact = invoice.SentToContact,
						CisDeduction = invoice.CisDeduction,
						ContactId = invoice.Contact?.Id,
					};

					if (!c_context.Invoices.Contains(inv))
					{
						c_context.Invoices.Add(inv);
					}

					if (invoice.LineItems != null)
					{
						foreach (var lineItem in invoice.LineItems)
						{
							try
							{
								var lItem = this.GetLineItem(lineItem, invoice.Id);
								if (!c_context.LineItems.Contains(lItem))
								{
									c_context.LineItems.Add(lItem);
									lineItemCount++;
								}

								foreach (var tracking in lineItem.Tracking)
								{
									var invoiceLineItemTrackingCategory = this.GetInvoiceLineItemTrackingCategory(lItem.LineItemId, tracking.Id, tracking.Option);
									var invoiceTrackingCategory = this.GetInvoiceTrackingCategory(tracking.Id, tracking.Name, org);
									var invoiceTrackingOption = this.GetInvoiceTrackingOption(tracking.Option, tracking.Id, org);
									if (!c_context.InvoiceLineItemTrackingCategories.Contains(invoiceLineItemTrackingCategory))
									{
										c_context.InvoiceLineItemTrackingCategories.Add(invoiceLineItemTrackingCategory);
									}

									if (!invoiceTrackingCategories.Any(o => o.Id == invoiceTrackingCategory.Id && o.OrgName == org))
									{
										invoiceTrackingCategories.Add(invoiceTrackingCategory);
										c_context.InvoiceTrackingCategories.Add(invoiceTrackingCategory);
									}

									if (!invoiceTrackingOptions.Any(o => o.TrackingCategoryId == invoiceTrackingOption.TrackingCategoryId && o.Name == invoiceTrackingOption.Name && o.OrgName == org))
									{
										invoiceTrackingOptions.Add(invoiceTrackingOption);
										c_context.InvoiceTrackingOptions.Add(invoiceTrackingOption);
									}
								}
							}
							catch (Exception lineItemEx)
							{
								c_log.LogWarning($"Error occured while processing lineItem (ID: {lineItem.LineItemId}) for invoice (count: {count}): {lineItemEx.Message} --> {lineItemEx}");
							}
						}
					}

					if (invoice.Overpayments != null)
					{
						foreach (var overpayment in invoice.Overpayments)
						{
							try
							{
								var oPayment = this.GetOverpayment(overpayment, invoice.Id);
								if (!overPayments.Any(x => x.Id == oPayment.Id && x.ReferenceId == oPayment.ReferenceId))
								{
									overPayments.Add(oPayment);
									overpaymentCount++;
								}

								if (overpayment.LineItems != null)
								{
									foreach (var lineItem in overpayment.LineItems)
									{
										var olineItems = this.GetLineItem(lineItem, overpayment.Id);
										if (!overPaymentLineItems.Any(x => x.LineItemId == olineItems.LineItemId))
										{
											overPaymentLineItems.Add(olineItems);
											lineItemCount++;
										}
									}
								}

								if (overpayment.Allocations != null)
								{
									int order = 0;
									foreach (var allocation in overpayment.Allocations)
									{
										var overpaymentAllocation = new SOAR.Shared.Xero.Common.Model.OverpaymentAllocation()
										{
											OverpaymentId = overpayment.Id,
											AllocationOrder = ++order,
											InvoiceId = allocation.Invoice?.Id,
											AppliedAmount = allocation.AppliedAmount,
											Date = allocation.Date,
											Amount = allocation.Amount
										};
										if (!overPaymentAllocations.Any(x => x.OverpaymentId == overpaymentAllocation.OverpaymentId))
										{
											overPaymentAllocations.Add(
												overpaymentAllocation);
											overpaymentAllocationCount++;
										}
									}
								}
							}
							catch (Exception OverpayentEx)
							{
								c_log.LogWarning($"Error occured while processing overpayment (ID: {overpayment.Id}) for invoice (count: {count}, ID: {invoice.Id}): {OverpayentEx.Message} --> {OverpayentEx}");
							}

						}
					}

					if (invoice.Prepayments != null)
					{
						foreach (var prepayment in invoice.Prepayments)
						{
							try
							{
								var ppayment = this.GetPrepayment(prepayment, invoice.Id);
								if (!prePayments.Any(x => x.Id == ppayment.Id && x.ReferenceId == ppayment.ReferenceId))
								{
									prePayments.Add(ppayment);
									prepaymentCount++;
								}

								if (prepayment.LineItems != null)
								{
									foreach (var lineItem in prepayment.LineItems)
									{
										var plineItem = this.GetLineItem(lineItem, prepayment.Id);
										if (!prePaymentLineItems.Any(x => x.LineItemId == plineItem.LineItemId))
										{
											prePaymentLineItems.Add(plineItem);
											lineItemCount++;
										}
									}
								}

								if (prepayment.Allocations != null)
								{
									int order = 0;
									foreach (var allocation in prepayment.Allocations)
									{
										var prepaymentAllocation = new SOAR.Shared.Xero.Common.Model.PrepaymentAllocation()
										{
											PrepaymentId = prepayment.Id,
											AllocationOrder = ++order,
											InvoiceId = allocation.Invoice?.Id,
											AppliedAmount = allocation.AppliedAmount,
											Date = allocation.Date,
											Amount = allocation.Amount
										};
										if (!prePaymentAllocations.Any(x => x.PrepaymentId == prepaymentAllocation.PrepaymentId && x.AllocationOrder == prepaymentAllocation.AllocationOrder))
										{
											prePaymentAllocations.Add(
												prepaymentAllocation
											);
											prepaymentAllocationCount++;
										}
									}
								}
							}
							catch (Exception PrepaymentEx)
							{
								c_log.LogWarning($"Error occured while processing overpayment (ID: {prepayment.Id}) for invoice (count: {count}, ID: {invoice.Id}): {PrepaymentEx.Message} --> {PrepaymentEx}");
							}
						}
					}

					if (invoice.CreditNotes != null)
					{
						foreach (var creditNote in invoice.CreditNotes)
						{
							try
							{
								var cNote = this.GetCreditNote(creditNote, invoice.Id);

								if (!creditNotes.Any(b => b.Id == cNote.Id && b.ReferenceId == cNote.ReferenceId))
								{
									creditNotes.Add(cNote);
									creditNoteCount++;
								}

								if (creditNote.LineItems != null)
								{
									foreach (var lineItem in creditNote.LineItems)
									{
										var cLine = this.GetLineItem(lineItem, creditNote.Id);
										if (!creditNoteLineItems.Any(b => b.LineItemId.Equals(cLine.LineItemId)))
										{
											creditNoteLineItems.Add(cLine);
											lineItemCount++;
										}
									}
								}

								if (creditNote.Allocations != null)
								{
									int order = 0;
									foreach (var allocation in creditNote.Allocations)
									{
										var cNoteAllocation = new SOAR.Shared.Xero.Common.Model.CreditNoteAllocation()
										{
											CreditNoteId = creditNote.Id,
											AllocationOrder = ++order,
											InvoiceId = allocation.Invoice?.Id,
											AppliedAmount = allocation.AppliedAmount,
											Date = allocation.Date,
											Amount = allocation.Amount
										};

										if (!creditNoteAllocations.Any(b => b.CreditNoteId == cNoteAllocation.CreditNoteId && b.AllocationOrder == cNoteAllocation.AllocationOrder))
										{
											creditNoteAllocations.Add(cNoteAllocation);
											creditNoteAllocationCount++;
										}
									}
								}

							}
							catch (Exception creditNoteEx)
							{
								c_log.LogWarning($"Error occured while processing credit note (ID: {creditNote.Id}) for invoice (count: {count}, ID: {invoice.Id}): {creditNoteEx.Message} --> {creditNoteEx}");
							}
						}
					}

					count++;
				}

				var uniqueOverPayments = overPayments.GroupBy(x => new { x.Id, x.ReferenceId }).Select(y => y.FirstOrDefault());
				var uniqueOverPaymentLineItems = overPaymentLineItems.GroupBy(x => x.LineItemId).Select(y => y.FirstOrDefault());
				var uniqueOverPaymentAllocations = overPaymentAllocations.GroupBy(x => new { x.OverpaymentId, x.AllocationOrder }).Select(y => y.FirstOrDefault());

				var uniquePrePayments = prePayments.GroupBy(x => new { x.Id, x.ReferenceId }).Select(y => y.FirstOrDefault());
				var uniquePrePaymentLineItems = prePaymentLineItems.GroupBy(x => x.LineItemId).Select(y => y.FirstOrDefault());
				var uniquePrePaymentAllocations = prePaymentAllocations.GroupBy(x => new { x.PrepaymentId, x.AllocationOrder }).Select(y => y.FirstOrDefault());

				var uniqueCreditNotes = creditNotes.GroupBy(x => new { x.Id, x.ReferenceId }).Select(y => y.FirstOrDefault());
				var uniqueCreditNoteLineItems = creditNoteLineItems.GroupBy(x => x.LineItemId).Select(y => y.FirstOrDefault());
				var uniqueCreditNoteAllocations = creditNoteAllocations.GroupBy(x => new { x.CreditNoteId, x.AllocationOrder }).Select(y => y.FirstOrDefault());

				c_context.Prepayments.AddRange(uniquePrePayments);
				c_context.LineItems.AddRange(uniquePrePaymentLineItems);
				c_context.PrepaymentAllocations.AddRange(uniquePrePaymentAllocations);
				await c_context.SaveChangesAsync();

				c_context.Overpayments.AddRange(uniqueOverPayments);
				c_context.LineItems.AddRange(uniqueOverPaymentLineItems);
				c_context.OverpaymentAllocations.AddRange(uniqueOverPaymentAllocations);
				await c_context.SaveChangesAsync();

				c_context.CreditNotes.AddRange(uniqueCreditNotes);
				c_context.LineItems.AddRange(uniqueCreditNoteLineItems);
				c_context.CreditNoteAllocations.AddRange(uniqueCreditNoteAllocations);
				await c_context.SaveChangesAsync();

				c_log.LogInformation($"Imported {count} invoice(s)");
				c_log.LogInformation($"Imported {lineItemCount} line item(s)");
				c_log.LogInformation($"Imported {overpaymentCount} overpayment(s)");
				c_log.LogInformation($"Imported {overpaymentAllocationCount} overpayment allocation(s)");
				c_log.LogInformation($"Imported {prepaymentCount} prepayment(s)");
				c_log.LogInformation($"Imported {prepaymentAllocationCount} prepayment allocation(s)");
				c_log.LogInformation($"Imported {creditNoteCount} credit note(s)");
				c_log.LogInformation($"Imported {creditNoteAllocationCount} credit note allocation(s)");

			}
			catch (Exception ex)
			{
				c_log.LogError($"Error occured while processing invoices (count: {count}): {ex.Message} --> {ex}");
				throw ex;
			}
		}

		private async Task ImportPayments(string orgName)
		{
			string org = c_config.GetValue<string>("XeroApi:Org");
			int windowPeriodInYears = c_config.GetValue<int>("XeroApi:WindowPeriodInYears");
			int deltaWindowPeriodInDays = c_config.GetValue<int>("XeroApi:DeltaWindowPeriodInDays");
			DateTime windowStartDate = c_syncMode == SyncMode.Full ? DateTime.Now.AddYears(-1 * windowPeriodInYears) : DateTime.Now.AddDays(deltaWindowPeriodInDays);

			var payments = await c_api.Payments
							.ModifiedSince(windowStartDate)
							.FindAsync();

			var existing = await c_context.Payments.ToArrayAsync();
			var existingIds = c_syncMode == SyncMode.Full ? existing.Select(x => x.Id) : existing.Select(x => x.Id).Intersect(payments.Select(x => x.Id));

			c_context.Payments.RemoveRange(existing.Where(x => existingIds.Contains(x.Id)));

			await c_context.SaveChangesAsync();

			var count = 0;
			foreach (var payment in payments)
			{
				c_context.Payments.Add(this.GetPayment(payment, org));
				count++;
			}
			await c_context.SaveChangesAsync();
			c_log.LogInformation($"Imported {count} payment(s)");
		}

		private async Task ImportReceipts()
		{
			var existing = await c_context.Receipts.ToArrayAsync();

			c_context.Receipts.RemoveRange(existing);

			await c_context.SaveChangesAsync();

			var count = 0;
			var lineItemCount = 0;
			var receipts = await c_api.Receipts.FindAsync();

			foreach (var receipt in receipts)
			{
				c_context.Receipts.Add(this.GetReceipt(receipt));

				if (receipt.LineItems != null)
				{
					foreach (var lineItem in receipt.LineItems)
					{
						c_context.LineItems.Add(this.GetLineItem(lineItem, receipt.Id));
						lineItemCount++;
					}
				}

				count++;
			}
			await c_context.SaveChangesAsync();
			c_log.LogInformation($"Imported {count} receipt(s)");
			c_log.LogInformation($"Imported {lineItemCount} line item(s)");
		}
		#endregion

		#region Private Map Methods
		private SOAR.Shared.Xero.Common.Model.Line GetLine(Line line, Guid referenceId)
		{
			return new SOAR.Shared.Xero.Common.Model.Line()
			{
				ReferenceId = referenceId,
				AccountId = line.AccountId,
				Id = line.Id,
				AccountCode = line.AccountCode,
				AccountType = line.AccountType.GetEnumMemberValue(),
				AccountName = line.AccountName,
				NetAmount = line.NetAmount,
				GrossAmount = line.GrossAmount,
				TaxAmount = line.TaxAmount,
				TaxType = line.TaxType,
				TaxName = line.TaxName,
				Amount = line.Amount,
				Description = line.Description
			};
		}

		private SOAR.Shared.Xero.Common.Model.LineItem GetLineItem(LineItem lineItem, Guid referenceId)
		{
			return new SOAR.Shared.Xero.Common.Model.LineItem()
			{
				LineItemId = lineItem.LineItemId,
				ReferenceId = referenceId,
				Description = lineItem.Description,
				Quantity = lineItem.Quantity,
				UnitAmount = lineItem.UnitAmount,
				AccountCode = lineItem.AccountCode,
				ItemCode = lineItem.ItemCode,
				TaxType = lineItem.TaxType,
				TaxAmount = lineItem.TaxAmount,
				LineAmount = lineItem.LineAmount,
				DiscountRate = lineItem.DiscountRate
			};
		}

		private SOAR.Shared.Xero.Common.Model.InvoiceLineItemTrackingCategory GetInvoiceLineItemTrackingCategory(Guid lineItemId, Guid trackingCategoryId, string option)
		{
			return new SOAR.Shared.Xero.Common.Model.InvoiceLineItemTrackingCategory()
			{
				LineItemId = lineItemId,
				TrackingCategoryId = trackingCategoryId,
				Option = option
			};
		}

		private SOAR.Shared.Xero.Common.Model.InvoiceTrackingCategory GetInvoiceTrackingCategory(Guid trackingCategoryId, string name, string orgName)
		{
			return new SOAR.Shared.Xero.Common.Model.InvoiceTrackingCategory()
			{
				Id = trackingCategoryId,
				Name = name,
				Status = "Active",
				OrgName = orgName,
				Audit_Created = DateTime.UtcNow
			};
		}

		private SOAR.Shared.Xero.Common.Model.InvoiceTrackingOption GetInvoiceTrackingOption(string option, Guid trackingCategoryId, string orgName)
		{
			return new SOAR.Shared.Xero.Common.Model.InvoiceTrackingOption()
			{
				Id = new Guid(),
				Name = option,
				Status = "Active",
				TrackingCategoryId = trackingCategoryId,
				OrgName = orgName,
				Audit_Created = DateTime.UtcNow
			};
		}

		private SOAR.Shared.Xero.Common.Model.Overpayment GetOverpayment(Overpayment overpayment, Guid referenceId)
		{
			return new SOAR.Shared.Xero.Common.Model.Overpayment()
			{
				Id = overpayment.Id,
				ReferenceId = referenceId,
				HasAttachments = overpayment.HasAttachments,
				RemainingCredit = overpayment.RemainingCredit,
				AppliedAmount = overpayment.AppliedAmount,
				Type = overpayment.Type.GetEnumMemberValue(),
				CurrencyRate = overpayment.CurrencyRate,
				CurrencyCode = overpayment.CurrencyCode,
				Total = overpayment.Total,
				TotalTax = overpayment.TotalTax,
				SubTotal = overpayment.SubTotal,
				LineAmountTypes = overpayment.LineAmountTypes.GetEnumMemberValue(),
				Status = overpayment.Status.GetEnumMemberValue(),
				Date = overpayment.Date,
				ContactId = overpayment.Contact?.Id,
				UpdatedDateUtc = overpayment.UpdatedDateUtc
			};
		}

		private SOAR.Shared.Xero.Common.Model.Prepayment GetPrepayment(Prepayment prepayment, Guid referenceId)
		{
			return new SOAR.Shared.Xero.Common.Model.Prepayment()
			{
				Id = prepayment.Id,
				ReferenceId = referenceId,
				HasAttachments = prepayment.HasAttachments,
				RemainingCredit = prepayment.RemainingCredit,
				AppliedAmount = prepayment.AppliedAmount,
				Type = prepayment.Type.GetEnumMemberValue(),
				CurrencyRate = prepayment.CurrencyRate,
				CurrencyCode = prepayment.CurrencyCode,
				Total = prepayment.Total,
				TotalTax = prepayment.TotalTax,
				SubTotal = prepayment.SubTotal,
				LineAmountTypes = prepayment.LineAmountTypes.GetEnumMemberValue(),
				Status = prepayment.Status.GetEnumMemberValue(),
				Date = prepayment.Date,
				ContactId = prepayment.Contact?.Id,
				UpdatedDateUtc = prepayment.UpdatedDateUtc
			};
		}

		private SOAR.Shared.Xero.Common.Model.CreditNote GetCreditNote(CreditNote creditNote, Guid referenceId)
		{
			return new SOAR.Shared.Xero.Common.Model.CreditNote()
			{
				Id = creditNote.Id,
				ReferenceId = referenceId,
				HasAttachments = creditNote.HasAttachments,
				RemainingCredit = creditNote.RemainingCredit,
				AppliedAmount = creditNote.AppliedAmount,
				Type = creditNote.Type.GetEnumMemberValue(),
				CurrencyRate = creditNote.CurrencyRate,
				CurrencyCode = creditNote.CurrencyCode,
				Total = creditNote.Total,
				TotalTax = creditNote.TotalTax,
				SubTotal = creditNote.SubTotal,
				LineAmountTypes = creditNote.LineAmountTypes.GetEnumMemberValue(),
				Status = creditNote.Status.GetEnumMemberValue(),
				Date = creditNote.Date,
				ContactId = creditNote.Contact?.Id,
				UpdatedDateUtc = creditNote.UpdatedDateUtc,
				BrandingThemeId = creditNote.BrandingThemeId,
				CisDeduction = creditNote.CisDeduction,
				DueDate = creditNote.DueDate,
				FullyPaidOnDate = creditNote.FullyPaidOnDate,
				Number = creditNote.Number,
				Reference = creditNote.Reference,
				SentToContact = creditNote.SentToContact
			};
		}

		private SOAR.Shared.Xero.Common.Model.Payment GetPayment(Payment payment, string orgName)
		{
			return new SOAR.Shared.Xero.Common.Model.Payment()
			{
				Id = payment.Id,
				OrgName = orgName,
				Type = payment.Type.GetEnumMemberValue(),
				Status = payment.Status.GetEnumMemberValue(),
				Date = payment.Date,
				CurrencyRate = payment.CurrencyRate,
				BankAmount = payment.BankAmount,
				Amount = payment.Amount,
				Reference = payment.Reference,
				IsReconciled = payment.IsReconciled,
				InvoiceId = payment.Invoice?.Id,
				CreditNoteId = payment.CreditNote?.Id,
				PrepaymentId = payment.Prepayment?.Id,
				OverpaymentId = payment.Overpayment?.Id,
				AccountId = payment.Account?.Id
			};
		}

		private SOAR.Shared.Xero.Common.Model.Receipt GetReceipt(Receipt receipt)
		{
			return new SOAR.Shared.Xero.Common.Model.Receipt()
			{
				Id = receipt.Id,
				Status = receipt.Status.GetEnumMemberValue(),
				Date = receipt.Date,
				Reference = receipt.Reference,
				ContactId = receipt.Contact?.Id,
				HasAttachments = receipt.HasAttachments,
				LineAmountTypes = receipt.LineAmountTypes.GetEnumMemberValue(),
				ReceiptNumber = receipt.ReceiptNumber,
				SubTotal = receipt.SubTotal,
				Total = receipt.Total,
				TotalTax = receipt.TotalTax,
				UpdatedDateUtc = receipt.UpdatedDateUtc,
				Url = receipt.Url,
				UserId = receipt.User?.Id
			};
		}
		#endregion
	}
}
