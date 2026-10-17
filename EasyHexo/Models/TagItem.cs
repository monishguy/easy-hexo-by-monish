namespace EasyHexo.Models;

/// <summary>标签输入框里被 Enter 锁定下来的一个标签。</summary>
public sealed class TagItem
{
    public TagItem(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public override string ToString() => Name;
}
