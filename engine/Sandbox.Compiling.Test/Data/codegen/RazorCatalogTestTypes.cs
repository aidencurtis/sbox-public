using Microsoft.AspNetCore.Components;
using Sandbox.UI;

namespace TestComponents;

public class BankGrid : Panel
{
	public RenderFragment Body { get; set; }
}

public class BankGridItem : Panel
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
