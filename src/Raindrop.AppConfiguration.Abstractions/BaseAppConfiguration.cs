using Microsoft.Extensions.Configuration;

namespace Raindrop.AppConfiguration.Abstractions;

public abstract class BaseAppConfiguration<T> where T : BaseAppConfiguration<T>
{
    public abstract void Bind(IConfiguration config);
}