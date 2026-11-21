// Nestation CC BY-NC 3.0
using System.Globalization;
using System.Reflection;
using Content.Shared.Damage.Components;

namespace Content.Server._Nestation.Reactor;

public static class NestationDamageReader
{
    private static readonly MemberInfo? TotalMember = FindMember();

    private static MemberInfo? FindMember()
    {
        var members = typeof(DamageableComponent)
            .GetMember("TotalDamage", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        return members.Length > 0 ? members[0] : null;
    }

    public static float Total(DamageableComponent component)
    {
        object? value = TotalMember switch
        {
            PropertyInfo property => property.GetValue(component),
            FieldInfo field => field.GetValue(component),
            _ => null,
        };

        if (value == null)
            return 0f;

        var method = value.GetType().GetMethod("Float", Type.EmptyTypes);

        if (method != null && method.Invoke(value, null) is float result)
            return result;

        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0f;
    }
}
