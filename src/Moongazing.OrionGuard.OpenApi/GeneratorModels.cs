#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Moongazing.OrionGuard.OpenApi.Emit;

namespace Moongazing.OrionGuard.OpenApi
{
    /// <summary>
    /// The shape of one accessible member on the validated type, captured from the semantic model so the
    /// emit stage can bind schema properties to real members without re-touching symbols. Equatable by
    /// value so it participates correctly in incremental-generator caching.
    /// </summary>
    internal readonly struct MemberShape : IEquatable<MemberShape>
    {
        public MemberShape(
            string name, MemberTypeCategory category, NumericKind numericKind, bool isReferenceTypeOrNullable)
        {
            Name = name;
            Category = category;
            NumericKind = numericKind;
            IsReferenceTypeOrNullable = isReferenceTypeOrNullable;
        }

        public string Name { get; }

        public MemberTypeCategory Category { get; }

        /// <summary>The specific numeric family when <see cref="Category"/> is
        /// <see cref="MemberTypeCategory.Numeric"/>; <see cref="NumericKind.None"/> otherwise.</summary>
        public NumericKind NumericKind { get; }

        /// <summary>True when the member can hold null (a reference type or <see cref="Nullable{T}"/>).</summary>
        public bool IsReferenceTypeOrNullable { get; }

        public bool Equals(MemberShape other) =>
            Name == other.Name
            && Category == other.Category
            && NumericKind == other.NumericKind
            && IsReferenceTypeOrNullable == other.IsReferenceTypeOrNullable;

        public override bool Equals(object? obj) => obj is MemberShape other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + Name.GetHashCode();
                hash = (hash * 31) + (int)Category;
                hash = (hash * 31) + (int)NumericKind;
                hash = (hash * 31) + IsReferenceTypeOrNullable.GetHashCode();
                return hash;
            }
        }
    }

    /// <summary>
    /// One enclosing type in a nested <c>[OpenApiValidator]</c> target's declaring-type path: the C#
    /// keyword (<c>class</c> / <c>struct</c> / <c>record</c> / <c>record struct</c>) and the simple name
    /// of a type the target is declared inside. The emitter reconstructs each link as a <c>partial</c>
    /// declaration so the generated partial lands inside the correct nesting and extends the user's type.
    /// Equatable by value so it participates in incremental-generator caching.
    /// </summary>
    internal readonly struct EnclosingType : IEquatable<EnclosingType>
    {
        public EnclosingType(string keyword, string name)
        {
            Keyword = keyword;
            Name = name;
        }

        /// <summary>The C# type keyword the partial must repeat (e.g. <c>class</c>, <c>struct</c>,
        /// <c>record</c>, <c>record struct</c>).</summary>
        public string Keyword { get; }

        /// <summary>The simple (unqualified) name of the enclosing type.</summary>
        public string Name { get; }

        public bool Equals(EnclosingType other) => Keyword == other.Keyword && Name == other.Name;

        public override bool Equals(object? obj) => obj is EnclosingType other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + Keyword.GetHashCode();
                hash = (hash * 31) + Name.GetHashCode();
                return hash;
            }
        }
    }

    /// <summary>
    /// The fully value-typed description of one <c>[OpenApiValidator]</c> target threaded through the
    /// incremental pipeline. Holds no Roslyn symbols or syntax, so equal targets across edits produce
    /// cache hits and the expensive parse/emit only re-runs when something it depends on changed.
    /// </summary>
    internal sealed class OpenApiValidatorTarget : IEquatable<OpenApiValidatorTarget>
    {
        public OpenApiValidatorTarget(
            string? namespaceName,
            string className,
            string validatedTypeFullName,
            bool isPartial,
            bool hasValidatedType,
            string accessibility,
            bool isGeneric,
            string documentPath,
            string schemaPointer,
            ImmutableArray<MemberShape> members,
            ImmutableArray<EnclosingType> enclosingTypes,
            string locationFilePath,
            int locationStart,
            int locationLength)
        {
            Namespace = namespaceName;
            ClassName = className;
            ValidatedTypeFullName = validatedTypeFullName;
            IsPartial = isPartial;
            HasValidatedType = hasValidatedType;
            Accessibility = accessibility;
            IsGeneric = isGeneric;
            DocumentPath = documentPath;
            SchemaPointer = schemaPointer;
            Members = members;
            EnclosingTypes = enclosingTypes;
            LocationFilePath = locationFilePath;
            LocationStart = locationStart;
            LocationLength = locationLength;
        }

        public string? Namespace { get; }

        public string ClassName { get; }

        /// <summary>The fully-qualified validated type (the single type argument the validator targets).</summary>
        public string ValidatedTypeFullName { get; }

        public bool IsPartial { get; }

        /// <summary>False when the validated type could not be inferred (no base class type argument).</summary>
        public bool HasValidatedType { get; }

        /// <summary>The C# accessibility keyword(s) of the user's partial, repeated on the generated partial
        /// so the two declarations agree (e.g. <c>public</c>, <c>internal</c>).</summary>
        public string Accessibility { get; }

        /// <summary>True when the target type itself is generic, or is nested inside a generic type.
        /// Generic targets are not supported yet (OG1010): generation is skipped for them rather than
        /// emitting a partial whose type parameters or constraints cannot be reconstructed.</summary>
        public bool IsGeneric { get; }

        public string DocumentPath { get; }

        public string SchemaPointer { get; }

        public ImmutableArray<MemberShape> Members { get; }

        /// <summary>The chain of enclosing types from the outermost (just inside the namespace) down to the
        /// type immediately containing the target. Empty for a namespace- or global-scope target. The
        /// emitter reconstructs these as nested <c>partial</c> declarations so the generated partial extends
        /// the user's actual (nested) type.</summary>
        public ImmutableArray<EnclosingType> EnclosingTypes { get; }

        // Location pieces, stored primitively so the target stays a pure value for caching. Reconstituted
        // into a Location only when a diagnostic is actually reported.
        public string LocationFilePath { get; }

        public int LocationStart { get; }

        public int LocationLength { get; }

        public Location GetLocation()
        {
            if (string.IsNullOrEmpty(LocationFilePath))
            {
                return Location.None;
            }

            return Location.Create(
                LocationFilePath,
                new Microsoft.CodeAnalysis.Text.TextSpan(LocationStart, LocationLength),
                new Microsoft.CodeAnalysis.Text.LinePositionSpan());
        }

        public bool Equals(OpenApiValidatorTarget? other)
        {
            if (other is null)
            {
                return false;
            }

            return Namespace == other.Namespace
                && ClassName == other.ClassName
                && ValidatedTypeFullName == other.ValidatedTypeFullName
                && IsPartial == other.IsPartial
                && HasValidatedType == other.HasValidatedType
                && Accessibility == other.Accessibility
                && IsGeneric == other.IsGeneric
                && DocumentPath == other.DocumentPath
                && SchemaPointer == other.SchemaPointer
                && LocationFilePath == other.LocationFilePath
                && LocationStart == other.LocationStart
                && LocationLength == other.LocationLength
                && Members.SequenceEqual(other.Members)
                && EnclosingTypes.SequenceEqual(other.EnclosingTypes);
        }

        public override bool Equals(object? obj) => Equals(obj as OpenApiValidatorTarget);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + (Namespace?.GetHashCode() ?? 0);
                hash = (hash * 31) + ClassName.GetHashCode();
                hash = (hash * 31) + ValidatedTypeFullName.GetHashCode();
                hash = (hash * 31) + IsPartial.GetHashCode();
                hash = (hash * 31) + Accessibility.GetHashCode();
                hash = (hash * 31) + IsGeneric.GetHashCode();
                hash = (hash * 31) + DocumentPath.GetHashCode();
                hash = (hash * 31) + SchemaPointer.GetHashCode();
                foreach (var member in Members)
                {
                    hash = (hash * 31) + member.GetHashCode();
                }

                foreach (var enclosing in EnclosingTypes)
                {
                    hash = (hash * 31) + enclosing.GetHashCode();
                }

                return hash;
            }
        }
    }

    /// <summary>
    /// One OpenAPI document's text plus its file name, captured from <see cref="AdditionalText"/> as a
    /// value so the pipeline only re-parses when a document's content actually changes.
    /// </summary>
    internal readonly struct OpenApiDocument : IEquatable<OpenApiDocument>
    {
        public OpenApiDocument(string fileName, string fullPath, string? content)
        {
            FileName = fileName;
            FullPath = fullPath;
            Content = content;
        }

        public string FileName { get; }

        public string FullPath { get; }

        /// <summary>The document text, or <c>null</c> when the file had no readable content.</summary>
        public string? Content { get; }

        public bool Equals(OpenApiDocument other) =>
            FileName == other.FileName && FullPath == other.FullPath && Content == other.Content;

        public override bool Equals(object? obj) => obj is OpenApiDocument other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + FileName.GetHashCode();
                hash = (hash * 31) + (Content?.GetHashCode() ?? 0);
                return hash;
            }
        }
    }
}
