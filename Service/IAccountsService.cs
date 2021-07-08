using System.Collections.Generic;
using SOARIntegration.Xero.Common.Model;
namespace SOARIntegration.Xero.Api.Accounts.Service
{
	public interface IAccountsService
	{
        void InsertAccounts(List<Account> accounts);
        IEnumerable<Account> GetAllAcccounts();
	}
}
