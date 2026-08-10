namespace Raindrop.AppConfiguration.Abstractions;

public interface IAppConfigurationManager<TOptions> where TOptions : class
{
    TOptions Config { get; }
}
