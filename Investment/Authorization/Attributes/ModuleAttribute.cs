namespace Investment.Authorization.Attributes
{
    [AttributeUsage(AttributeTargets.Class)]
    public class ModuleAttribute : Attribute
    {
        public string Name { get; }

        public ModuleAttribute(string name)
        {
            Name = name;
        }
    }
}
