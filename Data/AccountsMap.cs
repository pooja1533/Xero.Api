using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SOARIntegration.Xero.Common.Model;

namespace SOARIntegration.Xero.Api.Accounts.Data {
	public class AccountsMap {
		public AccountsMap (EntityTypeBuilder<Account> entityBuilder) {
			entityBuilder.HasKey (t => t.Id);
			entityBuilder.Property (t => t.Name).IsRequired ();
		}
	}
}