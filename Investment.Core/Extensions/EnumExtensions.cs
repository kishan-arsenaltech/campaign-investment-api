using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace Investment.Core.Extensions
{
    public static class EnumExtensions
    {
        public static string GetDisplayName(this Enum enumValue)
        {
            if (enumValue == null)
                return string.Empty;

            var type = enumValue.GetType();
            var name = enumValue.ToString();

            var member = type.GetMember(name).FirstOrDefault();

            if (member == null)
                return name;

            var attr = member.GetCustomAttribute<DisplayAttribute>(false);

            return string.IsNullOrWhiteSpace(attr?.Name)
                ? name
                : attr.Name;
        }
    }
}
