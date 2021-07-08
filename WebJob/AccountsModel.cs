using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using SOARIntegration.Xero.Api.Accounts.Service;
using Xero.Api.Core.Model;
using Xero.Api.Core;
using SOARIntegration.Xero.Api.Accounts.Repository;
using Microsoft.Extensions.Configuration;
using SOAR.Shared.Xero.Common.Model;
using static SOAR.Shared.Xero.Common.Model.Mode;

namespace SOARIntegration.Xero.Api.Accounts.WebJob {
	public class AccountsModel {
		private readonly ILogger<AccountsModel> logger;
		private readonly IAccountsService _accountService;
        private readonly IXeroCoreApi api;
        private readonly Context context;
        private readonly String company;
        private readonly IConfigurationRoot config;
        private readonly Mode.SyncMode mode;

        public AccountsModel(ILogger<AccountsModel> logger, Context ctx, IAccountsService accountService, String companyName, IConfigurationRoot cfg, string syncMode) {
			this.logger = logger;
            this.context = ctx;
			this._accountService = accountService;
            this.config = cfg;
            this.company = companyName;
            this.api = SOARIntegration.Xero.Common.Helpers.Application.Initialise(company);
            this.mode = syncMode == "-f" ? SyncMode.Full : SyncMode.Delta;
        }

        public void ProcessData() {

            try{
                logger.LogInformation("Running Account web job on {0}", DateTime.Now.ToString());

                int windowPeriodInYears = config.GetValue<int>("XeroApi:WindowPeriodInYears");
                int deltaWindowPeriodInDays = config.GetValue<int>("XeroApi:DeltaWindowPeriodInDays");
                DateTime windowStartDate = mode == SyncMode.Full ? DateTime.Now.AddYears(-1 * windowPeriodInYears) : DateTime.Now.AddDays(deltaWindowPeriodInDays);

                string orgName = config.GetValue<string>("XeroApi:Org");

                var response = api.Accounts
                    .ModifiedSince (windowStartDate)
                    .FindAsync();

                var responseAsList = response.Result.ToList();
                List<SOARIntegration.Xero.Common.Model.Account> accountList = new List<SOARIntegration.Xero.Common.Model.Account>();
                for (var count = 0; count < responseAsList.Count(); count++)
                {
                    var acc = mapResponseData(responseAsList[count], orgName);
                    if (acc != null)
                    { accountList.Add(acc);
                        logger.LogInformation($"{orgName} - Account[{acc.Name}]" );
                    }
                }
                if (accountList.Any())
                {
                    _accountService.InsertAccounts(accountList);
                }
                logger.LogInformation("Total Accounts are {0}", responseAsList.Count());

            }
            catch (Exception ex){
                Console.WriteLine( ex.Message);
            }


        }


        private SOARIntegration.Xero.Common.Model.Account mapResponseData(Account account, string org)
        {

            SOARIntegration.Xero.Common.Model.Account _account = new SOARIntegration.Xero.Common.Model.Account
            {
                AccountId = account.Id.ToString(),
                Code = account.Code,
                Name = account.Name,
                OrgName = org,
                Description = account.Description,
                Status = account.Status.ToString(),
                Type = account.Type.ToString(),
                BankAccountNumber = account.BankAccountNumber,
                BankAccountType = account.BankAccountNumber,
                CurrencyCode = account.CurrencyCode,
                TaxType = account.TaxType,
                EnablePaymentsToAccount = account.EnablePaymentsToAccount,
                ShowInExpenseClaims = account.ShowInExpenseClaims,
                ReportingCode = account.ReportingCode,
                ReportingCodeName = account.ReportingCodeName,
                HasAttachments = account.HasAttachments,
                SystemAccount = account.SystemAccount.HasValue ? account.SystemAccount.GetEnumMemberValue() : null,
                Class = account.Class.GetEnumMemberValue()
            };
            return _account;

        }
    }
}