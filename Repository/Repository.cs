using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using SOARIntegration.Xero.Common.Model;

namespace SOARIntegration.Xero.Api.Accounts.Repository
{
	public class Repository<T> : IRepository<T> where T : Account
	{
		private readonly Context context;
		private DbSet<T> entities;
		string errorMessage = string.Empty;

		public Repository(Context context)
		{
			this.context = context;
			entities = context.Set<T>();
		}

		public IEnumerable<T> GetAll()
		{
			return entities.AsEnumerable();
		}

		public T Get(String acountId)
		{
            return entities.SingleOrDefault (s => s.AccountId == acountId);
		}

		public void Insert(T entity)
		{
			if (entity == null) {
				throw new ArgumentNullException ("entity");
			}
            entity.Created = GetDefaultDate(entity.Created);
            entity.Modified = GetDefaultDate(entity.Modified);
            entities.Add(entity);
			context.SaveChanges();
		}

		public void Update(T entity)
		{
			if (entity == null)
			{
				throw new ArgumentNullException ("entity");
			}

            var local = context.Set<T>()
               .Local
               .FirstOrDefault(entry => entry.Id.Equals(entity.Id));

            if (local.Id > 0)
            {
                context.Entry(local).State = EntityState.Detached;
            }

            context.Entry(entity).State = EntityState.Modified;
            entity.Modified = GetDefaultDate(entity.Modified);

            context.SaveChanges ();
		}

		public void Delete(T entity)
		{
			if (entity == null)
			{
				throw new ArgumentNullException ("entity");
			}
			entities.Remove(entity);
			context.SaveChanges();
		}

		public void Remove(T entity)
		{
			if (entity == null)
			{
				throw new ArgumentNullException ("entity");
			}
			entities.Remove(entity);
		}

		public void SaveChanges()
		{
			context.SaveChanges ();
		}

        private DateTime GetDefaultDate(DateTime date)
        {
            return date == DateTime.MinValue ? DateTime.Now : date;
        }
    }
}