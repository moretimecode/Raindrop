namespace Raindrop.AppConfiguration;

public interface IConfigLoadErrorNotification<TOptions>
{
    void Send(Exception ex);
}
