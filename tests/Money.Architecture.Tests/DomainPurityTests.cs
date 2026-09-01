using System.Reflection;

namespace Money.Architecture.Tests;

public sealed class DomainPurityTests
{
    private static readonly Assembly Domain = typeof(Money.Domain.DomainAssemblyMarker).Assembly;

    private const BindingFlags All =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private const BindingFlags PublicMembers =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;

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

            // GetMethods() never returns constructors — ConstructorInfo is a separate
            // reflection surface. Without this, a constructor such as
            // `public Money(double amount)` that converts the value away without ever
            // storing it in a double-typed member would pass undetected, which is
            // exactly the "float touches money" shape spec D8 forbids.
            foreach (var ctor in type.GetConstructors(All))
            {
                foreach (var p in ctor.GetParameters())
                {
                    if (IsFloating(p.ParameterType)) offenders.Add($"{type.FullName}..ctor({p.Name})");
                }
            }
        }

        offenders.Should().BeEmpty("floating point must never touch money (spec D8)");

        static bool IsFloating(Type t)
        {
            t = Nullable.GetUnderlyingType(t) ?? t;
            if (t.IsByRef || t.IsArray) t = t.GetElementType() ?? t;
            if (t == typeof(double) || t == typeof(float) || t == typeof(Half)) return true;

            // A double/float can hide behind any generic wrapper — List<double>,
            // IReadOnlyList<double>, Dictionary<string, double>, Task<float> — not
            // just Nullable<T> and arrays. Recurse into type arguments so none of
            // those shapes evade the rule. (decimal is unaffected: it never matches
            // the direct check above, at any nesting depth.)
            if (t.IsGenericType) return t.GetGenericArguments().Any(IsFloating);

            return false;
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

        var offenders = new List<string>();

        foreach (var type in Domain.GetTypes())
        {
            // Type.IsPublic is false for *every* nested type regardless of its own
            // declared accessibility — a nested type reports through IsNestedPublic
            // instead, so `t.IsPublic` alone silently skips every nested public type.
            // IsVisible correctly answers "is this type reachable from outside the
            // assembly", including nested-public-inside-public-outer.
            if (!type.IsVisible) continue;

            foreach (var field in type.GetFields(PublicMembers))
            {
                if (IsMutableCollection(field.FieldType))
                    offenders.Add($"{type.FullName}.{field.Name}");
            }

            foreach (var property in type.GetProperties(PublicMembers))
            {
                if (IsMutableCollection(property.PropertyType))
                    offenders.Add($"{type.FullName}.{property.Name}");
            }

            foreach (var method in type.GetMethods(PublicMembers | BindingFlags.DeclaredOnly))
            {
                // Property/indexer/event accessors and operator overloads are
                // "special name" methods. Accessors are already caught above via the
                // property scan, reported under the property's own name — skip them
                // here so the same violation isn't reported twice under two names
                // (e.g. both "Tags" and "get_Tags").
                if (method.IsSpecialName) continue;

                if (IsMutableCollection(method.ReturnType))
                    offenders.Add($"{type.FullName}.{method.Name} (return)");
            }
        }

        offenders.Should().BeEmpty();

        bool IsMutableCollection(Type t)
        {
            if (t.IsArray) return true;
            if (!t.IsGenericType) return false;
            return mutable.Contains(t.GetGenericTypeDefinition());
        }
    }
}
