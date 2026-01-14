using Microsoft.AspNetCore.Razor.Language;
using System.Collections.Generic;

namespace Sandbox.Razor;

public static class RazorProcessor
{
	/// <summary>
	/// Generate the c# code from an incoming razor file
	/// </summary>
	public static string GenerateFromSource( string text, string filename, string rootNamespace = null, bool useFolderNamespacing = true )
	{
		return GenerateFromSource( text, filename, rootNamespace, useFolderNamespacing, (RazorCatalog)null );
	}

	public static string GenerateFromSource( string text, string filename, string rootNamespace, bool useFolderNamespacing, RazorCatalog catalog )
	{
		// If a root namespace is provided and the file doesn't have an namespace inject one
		if ( useFolderNamespacing )
		{
			text = AddNamespace( text, filename, rootNamespace );
		}

		var engine = GetEngine();

		RazorSourceDocument source = RazorSourceDocument.Create( text, filename );

		// We need to set Items before processing so codegen can read them.
		var defaultEngine = engine as Microsoft.AspNetCore.Razor.Language.DefaultRazorProjectEngine;
		if ( defaultEngine == null )
		{
			throw new System.InvalidOperationException( "Expected DefaultRazorProjectEngine" );
		}

		var code = defaultEngine.CreateCodeDocumentCore( source, FileKinds.Component, new List<RazorSourceDocument>(), new List<TagHelperDescriptor>() );
		code.SetCodeGenerationOptions( RazorCodeGenerationOptions.Create( o => { } ) );

		// Store the catalog on the document so codegen can access it
		if ( catalog != null )
		{
			code.Items["RazorCatalog"] = catalog;
		}

		defaultEngine.Engine.Process( code );

		RazorCSharpDocument document = code.GetCSharpDocument();

		return document.GeneratedCode;
	}

	private static string AddNamespace( string text, string filename, string rootNamespace )
	{
		if ( string.IsNullOrEmpty( rootNamespace ) ) return text;
		if ( text.Contains( "@namespace" ) ) return text;

		// Compute namespace from folder structure
		var directory = System.IO.Path.GetDirectoryName( filename );
		var computedNamespace = rootNamespace;

		if ( !string.IsNullOrEmpty( directory ) )
		{
			// Normalize path separators to forward slash
			directory = directory.Replace( '\\', '/' );

			// Split by path separator and filter out empty or invalid segments
			var folders = directory.Split( ['/'], System.StringSplitOptions.RemoveEmptyEntries );

			foreach ( var folder in folders )
			{
				// Only include folders that would make valid C# namespace identifiers
				if ( IsValidNamespaceSegment( folder ) )
				{
					computedNamespace += "." + folder;
				}
			}
		}

		return $"@namespace {computedNamespace}\n{text}";
	}

	/// <summary>
	/// Check if a folder name is valid as a C# namespace segment
	/// </summary>
	static bool IsValidNamespaceSegment( string segment )
	{
		if ( string.IsNullOrWhiteSpace( segment ) ) return false;
		if ( segment.Contains( ":" ) ) return false;
		if ( segment == "." || segment == ".." ) return false;
		if ( segment.Contains( " " ) || segment.Contains( "-" ) ) return false;
		if ( char.IsDigit( segment[0] ) ) return false;

		return true;
	}

	static RazorProjectEngine GetEngine()
	{
		var configuration = RazorConfiguration.Default;
		var razorProjectEngine = RazorProjectEngine.Create( configuration, RazorProjectFileSystem.Create( "." ) );

		return razorProjectEngine;
	}

}
