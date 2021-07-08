using System;
using System.Collections.Generic;
using SOARIntegration.Xero.Common.Model;

namespace SOARIntegration.Xero.Api.Accounts.Repository {
    public interface IRepository<T> where T : Account {
		IEnumerable<T> GetAll ();
		T Get (String accountId);
		void Insert (T entity);
		void Update (T entity);
		void Delete (T entity);
		void Remove (T entity);
		void SaveChanges ();
	}
}