//
// Mono.VisualC.Interop.CppType.cs: Abstracts a C++ type declaration
//
// Author:
//   Alexander Corrado (alexander.corrado@gmail.com)
//
// Copyright (C) 2010 Alexander Corrado
//

using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Reflection;
using System.Collections.Generic;

namespace Mono.VisualC.Interop {

	public enum CppModifiers {
                Const,
                Pointer,
		Array,
		Reference,
		Volatile,
		// ---
		Signed,
		Unsigned,
		Short,
		Long
        }
	public enum CppTypes {
		Unknown,
		Class,
		Struct,
		Enum,
		Union,
		Void,
		Bool,
		Char,
		Int,
		Float,
		Double
	}

	public struct CppType {

		public static Dictionary<string,CppModifiers> Tokenize = new Dictionary<string, CppModifiers> () {
			{ "\\*", CppModifiers.Pointer },
			{ "\\[\\s*\\]", CppModifiers.Array },
			{ "\\&", CppModifiers.Reference }
		};

		/*
		public static Dictionary<CppModifiers,string> Stringify = new Dictionary<CppModifiers, string> () {
			{ CppModifiers.Pointer, "*" },
			{ CppModifiers.Array, "[]" },
			{ CppModifiers.Reference, "&" }
		};
		*/

		// FIXME: Passing these as delegates allows for the flexibility of doing processing on the
		//  type (i.e. to correctly mangle the function pointer arguments if the managed type is a delegate),
		//  however this does not make it very easy to override the default mappings at runtime.
		public static List<Func<Type,CppType>> ManagedTypeMap = new List<Func<Type,CppType>> () {
			(t) => { return typeof (void).Equals (t)  ? CppTypes.Void   : CppTypes.Unknown;  },
			(t) => { return typeof (bool).Equals (t)  ? CppTypes.Bool   : CppTypes.Unknown;  },
			(t) => { return typeof (char).Equals (t)  ? CppTypes.Char   : CppTypes.Unknown;  },
			(t) => { return typeof (int).Equals (t)   ? CppTypes.Int    : CppTypes.Unknown;  },
			(t) => { return typeof (float).Equals (t) ? CppTypes.Float  : CppTypes.Unknown;  },
			(t) => { return typeof (double).Equals (t)? CppTypes.Double : CppTypes.Unknown;  },

			(t) => { return typeof (short).Equals (t) ? new CppType (CppModifiers.Short, CppTypes.Int) : CppTypes.Unknown; },
			(t) => { return typeof (long).Equals (t)  ? new CppType (CppModifiers.Long, CppTypes.Int)  : CppTypes.Unknown; },

			// strings mangle as "const char*" by default
			(t) => { return typeof (string).Equals (t)? new CppType (CppModifiers.Const, CppTypes.Char, CppModifiers.Pointer) : CppTypes.Unknown; },
			// StringBuilder gets "char*"
			(t) => { return typeof (StringBuilder).Equals (t)? new CppType (CppTypes.Char, CppModifiers.Pointer) : CppTypes.Unknown; },

			// delegate types get special treatment
			(t) => { return typeof (Delegate).IsAssignableFrom (t)? CppType.ForDelegate (t) : CppTypes.Unknown; },

			// ... and of course ICppObjects do too!
			// FIXME: We assume c++ class not struct. There should probably be an attribute
			//   we can apply to managed wrappers to indicate if the underlying C++ type is actually declared struct
			(t) => { return typeof (ICppObject).IsAssignableFrom (t)? new CppType (CppTypes.Class, t.Name, CppModifiers.Pointer) : CppTypes.Unknown; },

			// convert managed type modifiers to C++ type modifiers like so:
			//  ref types to C++ references
			//  pointer types to C++ pointers
			//  array types to C++ arrays
			(t) => {
				if (t.GetElementType () == null) return CppTypes.Unknown;
				CppType cppType = CppType.ForManagedType (t.GetElementType ());
				if (t.IsByRef) cppType.Modifiers.Add (CppModifiers.Reference);
				if (t.IsPointer) cppType.Modifiers.Add (CppModifiers.Pointer);
				if (t.IsArray) cppType.Modifiers.Add (CppModifiers.Array);
				return cppType;
			}
		};

		public CppTypes ElementType { get; set; }

		// if the ElementType is Union, Struct, Class, or Enum
		//  this will contain the name of said type
		public string ElementTypeName { get; set; }

		public List<CppModifiers> Modifiers { get; private set; }

		// here, you can pass in things like "const char*" or "const Foo * const"
		//  DISCLAIMER: this is really just for convenience for now, and is not meant to be able
		//  to parse even moderately complex C++ type declarations.
		public CppType (string type) : this (Regex.Split (type, "\\s+(?!\\])"))
		{
		}

		public CppType (params object[] cppTypeSpec)
		{
			ElementType = CppTypes.Unknown;
			ElementTypeName = null;

			Modifiers  = new List<CppModifiers> ();

			Parse (cppTypeSpec);
		}

		private void Parse (object [] modifiers)
		{
			for (int i = 0; i < modifiers.Length; i++) {

				if (modifiers [i] is CppModifiers) {
					Modifiers.Add ((CppModifiers)modifiers [i]);
					continue;
				}

				string strModifier = modifiers [i] as string;
				if (strModifier != null) {
					// FIXME: Use Enum.TryParse here if we decide to make this NET_4_0 only
					try {
						Modifiers.Add ((CppModifiers)Enum.Parse (typeof (CppModifiers), strModifier, true));
						continue;
					} catch { }
				}

				// must be a type name
				ParseType (modifiers [i]);
			}
		}

		private void ParseType (object type)
		{
			if (type is CppTypes) {
				ElementType = (CppTypes)type;
				ElementTypeName = null;
				return;
			}

			string strType = type as string;
			if (strType != null) {
				// strip tokens off type name
				foreach (var token in Tokenize) {
					foreach (var match in Regex.Matches (strType, token.Key))
						Modifiers.Add (token.Value);

					strType = Regex.Replace (strType, token.Key, string.Empty);
				}

				// FIXME: Use Enum.TryParse here if we decide to make this NET_4_0 only
				try {
					CppTypes parsed = (CppTypes)Enum.Parse (typeof (CppTypes), strType, true);
					ElementType = parsed;
					ElementTypeName = null;
					return;
				} catch { }

				// it's the element type name
				strType = strType.Trim ();
				if (!strType.Equals (string.Empty))
					ElementTypeName = strType;
				return;
			}


			Type managedType = type as Type;
			if (managedType != null) {
				CppType mapped = CppType.ForManagedType (managedType);
				Apply (mapped);
				return;
			}
		}

		// Applies the element type of the passed instance
		//  and combines its modifiers into this instance.
		//  Use when THIS instance may have attributes you want,
		//  but want the element type of the passed instance.
		public void Apply (CppType type)
		{
			ElementType = type.ElementType;
			ElementTypeName = type.ElementTypeName;
			if (Modifiers == null) Modifiers = new List<CppModifiers> ();

			List<CppModifiers> oldModifiers = Modifiers;
			Modifiers = type.Modifiers;
			Modifiers.AddRange (oldModifiers);
		}

		/*
		public override string ToString ()
		{
			StringBuilder cppTypeString = new StringBuilder ();

			cppTypeString.Append (Enum.GetName (typeof (CppTypes), ElementType).ToLower ());

			if (ElementTypeName != null)
				cppTypeString.Append (" ").Append (ElementTypeName);

			foreach (var modifier in Modifiers) {
				string stringified;
				if (!Stringify.TryGetValue (modifier, out stringified))
					stringified = Enum.GetName (typeof (CppModifiers), modifier).ToLower ();

				cppTypeString.Append (" ").Append (stringified);
			}

			return cppTypeString.ToString ();
		}
		*/

		public static CppType ForManagedType (Type type)
		{

			var mappedType = (from checkType in ManagedTypeMap
			                  where checkType (type).ElementType != CppTypes.Unknown
			                  select checkType (type)).FirstOrDefault ();

			if (mappedType.Modifiers == null)
				mappedType.Modifiers = new List<CppModifiers> ();

			return mappedType;
		}

		public static CppType ForDelegate (Type delType)
		{
			if (!typeof (Delegate).IsAssignableFrom (delType))
				throw new ArgumentException ("Argument must be a delegate type");

			throw new NotImplementedException ();
		}



		public static implicit operator CppType (CppTypes type) {
			return new CppType (type);
		}
	}
}

