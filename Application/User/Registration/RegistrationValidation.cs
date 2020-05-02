using Application.Validators;
using FluentValidation;

namespace Application.User.Registration
{
	public class RegistrationValidation : AbstractValidator<RegistrationCommand>
	{
		public RegistrationValidation()
		{
			RuleFor(x => x.DisplayName).NotEmpty();
			RuleFor(x => x.UserName).NotEmpty();
			RuleFor(x => x.Email).NotEmpty().EmailAddress();
			RuleFor(x => x.Password).NotEmpty().Password();
		}
	}
}