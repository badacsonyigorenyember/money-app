using System.Reflection;

namespace Money.Architecture.Tests;

public sealed class DomainPurityTests
{
    private static readonly Assembly Domain = typeof(Money.Domain.DomainAssemblyMarker).Assembly;

    private const BindingFlags All =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    [Fact]
    public void No_floating_point_types_appear_in_the_domain()
    {
        var offenders = new List<string>();

        foreach (var type in Domain.GetTypes())
        {
            foreach (var field in type.GetFields(All))
            {
                if (IsFloating(field.FieldType)) offenders.Add($"{type.FullName}.{field.Name}");
            }

            foreach (var property in type.GetProperties(All))
            {
                if (IsFloating(property.PropertyType)) offenders.Add($"{type.FullName}.{property.Name}");
            }

            foreach (var method in type.GetMethods(All | BindingFlags.DeclaredOnly))
            {
                if (IsFloating(method.ReturnType)) offenders.Add($"{type.FullName}.{method.Name} (return)");
                foreach (var p in method.GetParameters())
                {
                    if (IsFloating(p.ParameterType)) offenders.Add($"{type.FullName}.{method.Name}({p.Name})");
                }
            }
        }

        offenders.Should().BeEmpty("floating point must never touch money (spec D8)");

        static bool IsFloating(Type t)
        {
            t = Nullable.GetUnderlyingType(t) ?? t;
            if (t.IsByRef || t.IsArray) t = t.GetElementType() ?? t;
            return t == typeof(double) || t == typeof(float) || t == typeof(Half);
        }
    }

    [Fact]
    public void No_public_domain_type_exposes_a_mutable_collection()
    {
        var mutable = new[]
        {
            typeof(List<>), typeof(ICollection<>), typeof(IList<>),
            typeof(Dictionary<,>), typeof(IDictionary<,>), typeof(HashSet<>), typeof(ISet<>)
        };

        var offenders = Domain.GetTypes()
            .Where(t => t.IsPublic)
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                              .Select(p => (Type: t, Property: p)))
            .Where(x =>
            {
                var pt = x.Property.PropertyType;
                if (pt.IsArray) return true;
                if (!pt.IsGenericType) return false;
                return mutable.Contains(pt.GetGenericTypeDefinition());
            })
            .Select(x => $"{x.Type.FullName}.{x.Property.Name}")
            .ToArray();

        offenders.Should().BeEmpty();
    }
}
