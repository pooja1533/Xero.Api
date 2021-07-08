using Microsoft.EntityFrameworkCore;
using SOARIntegration.Xero.Common.Model;
using SOARIntegration.Xero.Api.Accounts.Data;
using SOAR.Shared.Xero.Common.Model;

namespace SOARIntegration.Xero.Api.Accounts.Repository
{
	public class Context : DbContext
	{
		public Context(DbContextOptions<Context> options) : base(options)
		{
		}

		protected override void OnModelCreating(ModelBuilder modelBuilder)
		{
			base.OnModelCreating(modelBuilder);
			new AccountsMap(modelBuilder.Entity<Account>());

			modelBuilder.Entity<ManualJournal>().HasKey("Id");
			modelBuilder.Entity<Line>().HasKey("AutoId");
			modelBuilder.Entity<LinkedTransaction>().HasKey("Id");
			modelBuilder.Entity<Journal>().HasKey("Id");
			modelBuilder.Entity<Item>().HasKey("Id");
			modelBuilder.Entity<PurchaseDetails>().HasKey("ItemId");
			modelBuilder.Entity<SalesDetails>().HasKey("ItemId");
			modelBuilder.Entity<Invoice>().HasKey("Id");
			modelBuilder.Entity<LineItem>().HasKey("LineItemId");
			modelBuilder.Entity<Payment>().HasKey("Id");
			modelBuilder.Entity<Overpayment>().HasKey("Id", "ReferenceId");
			modelBuilder.Entity<OverpaymentAllocation>().HasKey("OverpaymentId", "AllocationOrder");
			modelBuilder.Entity<Prepayment>().HasKey("Id", "ReferenceId");
			modelBuilder.Entity<PrepaymentAllocation>().HasKey("PrepaymentId", "AllocationOrder");
			modelBuilder.Entity<CreditNote>().HasKey("Id", "ReferenceId");
			modelBuilder.Entity<CreditNoteAllocation>().HasKey("CreditNoteId", "AllocationOrder");
			modelBuilder.Entity<Receipt>().HasKey("Id");
		}

		public DbSet<ManualJournal> ManualJournals { get; set; }
		public DbSet<Line> Lines { get; set; }
		public DbSet<LinkedTransaction> LinkedTransactions { get; set; }
		public DbSet<Journal> Journals { get; set; }
		public DbSet<Item> Items { get; set; }
		public DbSet<PurchaseDetails> PurchaseDetails { get; set; }
		public DbSet<SalesDetails> SalesDetails { get; set; }
		public DbSet<Invoice> Invoices { get; set; }
		public DbSet<LineItem> LineItems { get; set; }
		public DbSet<Payment> Payments { get; set; }
		public DbSet<Overpayment> Overpayments { get; set; }
		public DbSet<OverpaymentAllocation> OverpaymentAllocations { get; set; }
		public DbSet<Prepayment> Prepayments { get; set; }
		public DbSet<PrepaymentAllocation> PrepaymentAllocations { get; set; }
		public DbSet<CreditNote> CreditNotes { get; set; }
		public DbSet<CreditNoteAllocation> CreditNoteAllocations { get; set; }
		public DbSet<Receipt> Receipts { get; set; }
		public DbSet<InvoiceLineItemTrackingCategory> InvoiceLineItemTrackingCategories { get; set; }
		public DbSet<InvoiceTrackingCategory> InvoiceTrackingCategories { get; set; }
		public DbSet<InvoiceTrackingOption> InvoiceTrackingOptions { get; set; }
	}
}
