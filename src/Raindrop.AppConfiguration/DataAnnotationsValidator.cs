using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace Raindrop.AppConfiguration;

public class DataAnnotationsValidator : IDataAnnotationsValidator
{
    public bool TryValidateObject(
        object obj,
        ICollection<ValidationResult> results,
        IDictionary<object, object?>? validationContextItems = null)
    {
        return Validator.TryValidateObject(
            obj,
            new ValidationContext(obj, null, validationContextItems),
            results,
            true);
    }

    public bool TryValidateObjectRecursive<T>(
        T obj,
        List<ValidationResult> results,
        IDictionary<object, object?>? validationContextItems = null)
    {
        return TryValidateObjectRecursive(obj, results, new HashSet<object>(), validationContextItems);
    }

    private bool TryValidateObjectRecursive<T>(
        T obj,
        List<ValidationResult> results,
        ISet<object> validatedObjects,
        IDictionary<object, object?>? validationContextItems = null)
    {
        if (obj is null)
        {
            return true;
        }

        // 短路：避免循环引用导致无限递归
        if (validatedObjects.Contains(obj))
        {
            return true;
        }

        validatedObjects.Add(obj);
        bool result = TryValidateObject(obj, results, validationContextItems);

        var properties = obj.GetType().GetProperties()
            .Where(prop => prop.CanRead && prop.GetIndexParameters().Length == 0)
            .ToList();

        foreach (var property in properties)
        {
            if (property.PropertyType == typeof(string) || property.PropertyType.IsValueType)
            {
                continue;
            }

            var value = property.GetValue(obj);

            if (value is null)
            {
                continue;
            }

            if (value is IEnumerable asEnumerable && value is not string)
            {
                foreach (var enumObj in asEnumerable)
                {
                    if (enumObj is null) continue;

                    var nestedResults = new List<ValidationResult>();
                    if (!TryValidateObjectRecursive(enumObj, nestedResults, validatedObjects, validationContextItems))
                    {
                        result = false;
                        foreach (var validationResult in nestedResults)
                        {
                            PropertyInfo prop = property;
                            results.Add(new ValidationResult(
                                validationResult.ErrorMessage,
                                validationResult.MemberNames.Select(x => prop.Name + '.' + x)));
                        }
                    }
                }
            }
            else
            {
                var nestedResults = new List<ValidationResult>();
                if (!TryValidateObjectRecursive(value, nestedResults, validatedObjects, validationContextItems))
                {
                    result = false;
                    foreach (var validationResult in nestedResults)
                    {
                        PropertyInfo prop = property;
                        results.Add(new ValidationResult(
                            validationResult.ErrorMessage,
                            validationResult.MemberNames.Select(x => prop.Name + '.' + x)));
                    }
                }
            }
        }

        return result;
    }
}

public interface IDataAnnotationsValidator
{
    bool TryValidateObject(
        object obj,
        ICollection<ValidationResult> results,
        IDictionary<object, object?>? validationContextItems = null);

    bool TryValidateObjectRecursive<T>(
        T obj,
        List<ValidationResult> results,
        IDictionary<object, object?>? validationContextItems = null);
}
