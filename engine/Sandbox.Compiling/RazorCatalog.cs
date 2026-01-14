using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Sandbox;

namespace Sandbox.Compiling;

/// <summary>
/// Builds a <see cref="Sandbox.Razor.RazorCatalog"/> from the current compilation and available .razor sources.
/// </summary>
internal static class RazorCatalogBuilder
{
	public static Sandbox.Razor.RazorCatalog Build( CSharpCompilation compilation, IReadOnlyList<CodeArchive.AdditionalFile> razorFiles )
	{
		var knownComponentTags = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var slotsByComponentTag = new Dictionary<string, HashSet<string>>( StringComparer.OrdinalIgnoreCase );

		// 1) All Panel subclasses from compilation + references
		CollectPanelSubtypes( compilation, knownComponentTags, slotsByComponentTag );

		// 2) Add implied component tags from .razor files (simple filename)
		foreach ( var file in razorFiles )
		{
			var tagName = System.IO.Path.GetFileNameWithoutExtension( file.LocalPath );
			if ( string.IsNullOrWhiteSpace( tagName ) )
				continue;

			knownComponentTags.Add( tagName );

			// Collect slots declared inside @code blocks for this component
			if ( !string.IsNullOrEmpty( file.Text ) )
			{
				var slots = RazorSlotsParser.TryExtractSlotNamesFromRazor( file.Text );
				if ( slots.Count > 0 )
				{
					if ( !slotsByComponentTag.TryGetValue( tagName, out var set ) )
					{
						set = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
						slotsByComponentTag[tagName] = set;
					}

					foreach ( var s in slots )
						set.Add( s );
				}
			}
		}

		return new Sandbox.Razor.RazorCatalog( knownComponentTags, slotsByComponentTag );
	}

	private static void CollectPanelSubtypes( CSharpCompilation compilation, HashSet<string> knownComponentTags, Dictionary<string, HashSet<string>> slotsByComponentTag )
	{
		var panelSymbol = compilation.GetTypeByMetadataName( "Sandbox.UI.Panel" );
		if ( panelSymbol == null )
			return;

		// Source assembly + referenced assemblies
		var assemblies = new List<IAssemblySymbol> { compilation.Assembly };
		assemblies.AddRange( compilation.SourceModule.ReferencedAssemblySymbols );

		foreach ( var assembly in assemblies )
		{
			foreach ( var type in EnumerateAllTypes( assembly.GlobalNamespace ) )
			{
				if ( type.IsStatic || type.IsAbstract )
					continue;

				if ( !type.InheritsFromOrEquals( panelSymbol ) )
					continue;

				// Must have a public parameterless ctor for OpenElement<T>()
				if ( !type.Constructors.Any( c => c.Parameters.IsEmpty && c.DeclaredAccessibility == Accessibility.Public ) )
					continue;

				knownComponentTags.Add( type.Name );

				var slots = CollectRenderFragmentSlots( type );
				if ( slots.Count > 0 )
				{
					if ( !slotsByComponentTag.TryGetValue( type.Name, out var set ) )
					{
						set = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
						slotsByComponentTag[type.Name] = set;
					}

					foreach ( var s in slots )
						set.Add( s );
				}
			}
		}
	}

	private static HashSet<string> CollectRenderFragmentSlots( INamedTypeSymbol type )
	{
		var slots = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		foreach ( var member in type.GetMembers() )
		{
			if ( member is IPropertySymbol prop )
			{
				if ( !IsRenderFragmentType( prop.Type ) )
					continue;

				// Needs to be assignable from generated code
				if ( prop.SetMethod == null )
					continue;
				if ( !IsAssignableFromGeneratedCode( prop.SetMethod.DeclaredAccessibility ) )
					continue;

				slots.Add( prop.Name );
			}
			else if ( member is IFieldSymbol field )
			{
				if ( !IsRenderFragmentType( field.Type ) )
					continue;
				if ( !IsAssignableFromGeneratedCode( field.DeclaredAccessibility ) )
					continue;

				slots.Add( field.Name );
			}
		}

		return slots;
	}

	private static bool IsAssignableFromGeneratedCode( Accessibility accessibility )
	{
		return accessibility == Accessibility.Public ||
			accessibility == Accessibility.Internal ||
			accessibility == Accessibility.Protected ||
			accessibility == Accessibility.ProtectedOrInternal;
	}

	private static bool IsRenderFragmentType( ITypeSymbol type )
	{
		if ( type is null ) return false;

		if ( type.Name == "RenderFragment" && type.ContainingNamespace?.ToDisplayString() == "Microsoft.AspNetCore.Components" )
			return true;

		var original = type.OriginalDefinition;
		if ( original?.Name == "RenderFragment" && original.ContainingNamespace?.ToDisplayString() == "Microsoft.AspNetCore.Components" )
			return true;

		return false;
	}

	private static IEnumerable<INamedTypeSymbol> EnumerateAllTypes( INamespaceSymbol ns )
	{
		foreach ( var type in ns.GetTypeMembers() )
		{
			yield return type;
			foreach ( var nested in EnumerateNestedTypes( type ) )
				yield return nested;
		}

		foreach ( var nestedNs in ns.GetNamespaceMembers() )
		{
			foreach ( var type in EnumerateAllTypes( nestedNs ) )
				yield return type;
		}
	}

	private static IEnumerable<INamedTypeSymbol> EnumerateNestedTypes( INamedTypeSymbol type )
	{
		foreach ( var nested in type.GetTypeMembers() )
		{
			yield return nested;
			foreach ( var deeper in EnumerateNestedTypes( nested ) )
				yield return deeper;
		}
	}

	private static bool InheritsFromOrEquals( this ITypeSymbol derived, ITypeSymbol baseType )
	{
		return derived != null && (SymbolEqualityComparer.Default.Equals( derived, baseType ) || derived.BaseType?.InheritsFromOrEquals( baseType ) == true);
	}

	private static class RazorSlotsParser
	{
		public static HashSet<string> TryExtractSlotNamesFromRazor( string razorText )
		{
			// Cheap parser: find @code { ... } blocks and scan for "RenderFragment" member declarations.
			// This intentionally ignores [Parameter] because s&box treats RenderFragment members as slots regardless.
			var slots = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
			var idx = 0;

			while ( true )
			{
				idx = razorText.IndexOf( "@code", idx, StringComparison.Ordinal );
				if ( idx < 0 ) break;

				var braceStart = razorText.IndexOf( '{', idx );
				if ( braceStart < 0 ) break;

				var braceEnd = FindMatchingBrace( razorText, braceStart );
				if ( braceEnd < 0 ) break;

				var block = razorText.Substring( braceStart + 1, braceEnd - braceStart - 1 );
				ExtractRenderFragmentMemberNamesFromCode( block, slots );

				idx = braceEnd + 1;
			}

			return slots;
		}

		private static int FindMatchingBrace( string text, int openBraceIndex )
		{
			var depth = 0;
			for ( var i = openBraceIndex; i < text.Length; i++ )
			{
				var c = text[i];
				if ( c == '{' ) depth++;
				else if ( c == '}' )
				{
					depth--;
					if ( depth == 0 ) return i;
				}
			}

			return -1;
		}

		private static void ExtractRenderFragmentMemberNamesFromCode( string code, HashSet<string> slots )
		{
			// Very lightweight scan to avoid adding Roslyn parsing here.
			// Matches patterns like:
			//   public RenderFragment Body { get; set; }
			//   RenderFragment Left;
			//   public RenderFragment<object> Item { get; set; }
			var lines = code.Split( new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries );
			foreach ( var rawLine in lines )
			{
				var line = rawLine.Trim();
				if ( line.Contains( "RenderFragment", StringComparison.Ordinal ) == false )
					continue;

				// Strip attributes and modifiers crudely
				// Find the token immediately after RenderFragment or RenderFragment<...>
				var rfIdx = line.IndexOf( "RenderFragment", StringComparison.Ordinal );
				if ( rfIdx < 0 ) continue;

				var after = line.Substring( rfIdx + "RenderFragment".Length );
				if ( after.StartsWith( "<" ) )
				{
					var gt = after.IndexOf( '>' );
					if ( gt < 0 ) continue;
					after = after.Substring( gt + 1 );
				}

				after = after.TrimStart();
				if ( after.Length == 0 ) continue;

				// Now 'after' should start with the member name
				var name = new string( after.TakeWhile( ch => char.IsLetterOrDigit( ch ) || ch == '_' ).ToArray() );
				if ( string.IsNullOrWhiteSpace( name ) ) continue;

				slots.Add( name );
			}
		}
	}
}
