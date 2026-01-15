using Microsoft.AspNetCore.Components;
using Sandbox.UI;

namespace TestComponents;

public class ParentPanel : Panel
{
	public RenderFragment Body { get; set; }
}

public class ChildPanel : Panel
{
}

// Used to verify slot precedence over component name
public class Body : Panel
{
}

public class Page : Panel
{
	public RenderFragment Body { get; set; }
	public RenderFragment Left { get; set; }
}

public class BaseWithSlot : Panel
{
	public RenderFragment Header { get; set; }
}

public class DerivedWithSlot : BaseWithSlot
{
}

public class HostPanel : Panel
{
}

public class OuterPanel : Panel
{
}

public class MiddlePanel : Panel
{
}

public class InnerPanel : Panel
{
}
