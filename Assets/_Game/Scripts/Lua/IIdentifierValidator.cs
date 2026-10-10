public interface IIdentifierValidator
{
    bool IsValid(string identifier);
    string GetErrorMessage(string identifier);
}