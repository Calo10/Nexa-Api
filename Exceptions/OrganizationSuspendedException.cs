namespace NexaApi.Exceptions;

public class OrganizationSuspendedException : Exception
{
    public const string ErrorCode = "account_suspended";

    public OrganizationSuspendedException()
        : base("This account is suspended. Please contact your administrator.")
    {
    }
}
