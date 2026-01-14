using System;
using System.Collections.Generic;

namespace Sandbox.Razor;

/// <summary>
/// Minimal catalog of known component tags and slot names used during Razor code generation.
/// This lives in Sandbox.Razor so callers can pass it in without introducing reverse dependencies.
/// </summary>
public sealed class RazorCatalog
{
	private readonly HashSet<string> _knownComponentTags;
	private readonly Dictionary<string, HashSet<string>> _slotsByComponentTag;

	public RazorCatalog( HashSet<string> knownComponentTags, Dictionary<string, HashSet<string>> slotsByComponentTag )
	{
		_knownComponentTags = knownComponentTags ?? new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		_slotsByComponentTag = slotsByComponentTag ?? new Dictionary<string, HashSet<string>>( StringComparer.OrdinalIgnoreCase );
	}

	public bool IsKnownComponent( string tagName )
	{
		if ( string.IsNullOrWhiteSpace( tagName ) ) return false;
		return _knownComponentTags.Contains( tagName );
	}

	public bool IsSlot( string parentTypeNameOrTag, string slotName )
	{
		if ( string.IsNullOrWhiteSpace( parentTypeNameOrTag ) ) return false;
		if ( string.IsNullOrWhiteSpace( slotName ) ) return false;

		var parentTag = NormalizeTypeNameToTag( parentTypeNameOrTag );
		if ( parentTag is null ) return false;

		return _slotsByComponentTag.TryGetValue( parentTag, out var slots ) && slots.Contains( slotName );
	}

	private static string NormalizeTypeNameToTag( string typeNameOrTag )
	{
		var s = typeNameOrTag.Trim();

		// Remove global qualification if present
		const string globalPrefix = "global::";
		if ( s.StartsWith( globalPrefix, StringComparison.Ordinal ) )
			s = s.Substring( globalPrefix.Length );

		// Strip generic arguments: Foo<Bar> -> Foo
		var genericStart = s.IndexOf( '<' );
		if ( genericStart >= 0 )
			s = s.Substring( 0, genericStart );

		// Namespace-qualified -> simple name
		var lastDot = s.LastIndexOf( '.' );
		if ( lastDot >= 0 )
			s = s.Substring( lastDot + 1 );

		return string.IsNullOrWhiteSpace( s ) ? null : s;
	}
}
