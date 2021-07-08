using SOARIntegration.Xero.Common.Model;
using SOARIntegration.Xero.Api.Accounts.Repository;
using System.Collections.Generic;
using System;

namespace SOARIntegration.Xero.Api.Accounts.Service
{
	public class AccountsService : IAccountsService
	{
		private IRepository<Account> _repository;

		public AccountsService(IRepository<Account> repository)
		{
			this._repository = repository;
		}

        public void InsertAccounts(List<Account> accounts)
        {
            for (var count = 0; count < accounts.Count; count++)
            {
                try
                {
                    var account = _repository.Get(accounts[count].AccountId);
                    if (account == null)
                    {
                        _repository.Insert(accounts[count]);
                    }
                    else
                    {
                        accounts[count].Id = account.Id;
                        accounts[count].Created = account.Created;
                        _repository.Update(accounts[count]);
                    }
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }
        }


        public IEnumerable<Account> GetAllAcccounts()
		{
			return _repository.GetAll();
		}
	}
}
