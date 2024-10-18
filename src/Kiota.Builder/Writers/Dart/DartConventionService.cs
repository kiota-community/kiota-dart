﻿using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Linq;

using Kiota.Builder.CodeDOM;
using Kiota.Builder.Extensions;

using static Kiota.Builder.CodeDOM.CodeTypeBase;

namespace Kiota.Builder.Writers.Dart;
public class DartConventionService : CommonLanguageConventionService
{
    internal static string AutoGenerationHeader => "// auto generated";
    internal static readonly HashSet<string> ErrorClassProperties = new(StringComparer.OrdinalIgnoreCase) { "message", "statusCode", "responseHeaders", "innerExceptions" };

    public override string StreamTypeName => "stream";
    public override string VoidTypeName => "void";
    public override string DocCommentPrefix => "/// ";
    private static readonly HashSet<string> NullableTypes = new(StringComparer.OrdinalIgnoreCase) { "int", "bool", "double", "string", "datetime", "dateonly", "timeonly" };
    public const char NullableMarker = '?';
    public static string NullableMarkerAsString => "?";
    public override string ParseNodeInterfaceName => "ParseNode";

    private const string ReferenceTypePrefix = "[";
    private const string ReferenceTypeSuffix = "]";

    public override bool WriteShortDescription(IDocumentedElement element, LanguageWriter writer, string prefix = "", string suffix = "")
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(element);
        if (!element.Documentation.DescriptionAvailable) return false;
        if (element is not CodeElement codeElement) return false;

        var description = element.Documentation.GetDescription(x => GetTypeReferenceForDocComment(x, codeElement), ReferenceTypePrefix, ReferenceTypeSuffix);
        writer.WriteLine($"{DocCommentPrefix} {description}");

        return true;
    }

    public void WriteLongDescription(CodeElement element, LanguageWriter writer, IEnumerable<string>? additionalRemarks = default)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (element is not IDocumentedElement documentedElement || documentedElement.Documentation is not CodeDocumentation documentation) return;
        if (additionalRemarks == default)
            additionalRemarks = [];
        var remarks = additionalRemarks.Where(static x => !string.IsNullOrEmpty(x)).ToArray();
        if (documentation.DescriptionAvailable || documentation.ExternalDocumentationAvailable || remarks.Length != 0)
        {
            if (documentation.DescriptionAvailable)
            {
                var description = documentedElement.Documentation.GetDescription(x => GetTypeReferenceForDocComment(x, element), ReferenceTypePrefix, ReferenceTypeSuffix);
                writer.WriteLine($"{DocCommentPrefix}{description}");
            }
            foreach (var additionalRemark in remarks)
                writer.WriteLine($"{DocCommentPrefix}{additionalRemark}");
            if (element is IDeprecableElement deprecableElement && deprecableElement.Deprecation is not null && deprecableElement.Deprecation.IsDeprecated)
                foreach (var additionalComment in GetDeprecationInformationForDocumentationComment(deprecableElement))
                    writer.WriteLine($"{DocCommentPrefix}{additionalComment}");

            if (documentation.ExternalDocumentationAvailable)
                writer.WriteLine($"{DocCommentPrefix}@see <a href=\"{documentation.DocumentationLink}\">{documentation.DocumentationLabel}</a>");
        }
    }

    internal string GetTypeReferenceForDocComment(CodeTypeBase code, CodeElement targetElement)
    {
        if (code is CodeType codeType && codeType.TypeDefinition is CodeMethod method)
            return $"{GetTypeString(new CodeType { TypeDefinition = method.Parent, IsExternal = false }, targetElement)}#{GetTypeString(code, targetElement)}";
        return $"{GetTypeString(code, targetElement)}";
    }

    private string[] GetDeprecationInformationForDocumentationComment(IDeprecableElement element)
    {
        if (element.Deprecation is null || !element.Deprecation.IsDeprecated) return Array.Empty<string>();

        var versionComment = string.IsNullOrEmpty(element.Deprecation.Version) ? string.Empty : $" as of {element.Deprecation.Version}";
        var dateComment = element.Deprecation.Date is null ? string.Empty : $" on {element.Deprecation.Date.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
        var removalComment = element.Deprecation.RemovalDate is null ? string.Empty : $" and will be removed {element.Deprecation.RemovalDate.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
        return [
            $"@deprecated",
            $"{element.Deprecation.GetDescription(type => GetTypeString(type, (element as CodeElement)!))}{versionComment}{dateComment}{removalComment}"
        ];
    }

    public override string GetAccessModifier(AccessModifier access)
    {
        // Dart does not support access modifiers
        return "";
    }

#pragma warning disable CA1822 // Method should be static
    public string GetAccessModifierPrefix(AccessModifier access)
    {
        return access switch
        {
            AccessModifier.Private => "_",
            _ => string.Empty,
        };
    }

    public string GetAccessModifierAttribute(AccessModifier access)
    {
        return string.Empty;
    }

    internal void AddRequestBuilderBody(CodeClass parentClass, string returnType, LanguageWriter writer, string? urlTemplateVarName = default, string? prefix = default, IEnumerable<CodeParameter>? pathParameters = default, IEnumerable<CodeParameter>? customParameters = default)
    {
        if (parentClass.GetPropertyOfKind(CodePropertyKind.PathParameters) is CodeProperty pathParametersProp &&
            parentClass.GetPropertyOfKind(CodePropertyKind.RequestAdapter) is CodeProperty requestAdapterProp)
        {
            var pathParametersSuffix = !(pathParameters?.Any() ?? false) ? string.Empty :
            $", {string.Join(", ", pathParameters.Select(x => x.Optional ? $"{x.Name.ToFirstCharacterLowerCase()} : {x.Name.ToFirstCharacterLowerCase()}" : $"{x.Name.ToFirstCharacterLowerCase()}"))}";
            var urlTplRef = string.IsNullOrEmpty(urlTemplateVarName) ? pathParametersProp.Name.ToFirstCharacterLowerCase() : urlTemplateVarName;
            if (customParameters?.Any() ?? false)
            {
                urlTplRef = TempDictionaryVarName;
                writer.WriteLine($"var {urlTplRef} = Map.of({pathParametersProp.Name.ToFirstCharacterLowerCase()});");
                foreach (var param in customParameters)
                    writer.WriteLine($"{urlTplRef}.putIfAbsent(\"{param.Name.ToFirstCharacterLowerCase()}\", ()=> {param.Name.ToFirstCharacterLowerCase()});");
            }
            writer.WriteLine($"{prefix}{returnType}({urlTplRef}, {requestAdapterProp.Name.ToFirstCharacterLowerCase()}{pathParametersSuffix});");
        }
    }
    public override string TempDictionaryVarName => "urlTplParams";
    internal void AddParametersAssignment(LanguageWriter writer, CodeTypeBase pathParametersType, string pathParametersReference, string varName = "", params (CodeTypeBase, string, string)[] parameters)
    {
        if (pathParametersType == null) return;
        if (string.IsNullOrEmpty(varName))
        {
            varName = TempDictionaryVarName;
            writer.WriteLine($"var {varName} = {pathParametersType.Name}({pathParametersReference});");
        }
        if (parameters.Length != 0)
        {
            writer.WriteLines(parameters.Select(p =>
            {
                var (ct, name, identName) = p;
                string nullCheck = string.Empty;
                if (ct.CollectionKind == CodeTypeCollectionKind.None && ct.IsNullable)
                {
                    if (nameof(String).Equals(ct.Name, StringComparison.OrdinalIgnoreCase))
                        nullCheck = $"if ({identName}!= null && {identName}.isNotEmpty) ";
                    else
                        nullCheck = $"if ({identName} != null) ";
                }
                return $"{nullCheck}{varName}[\"{name}\"]={identName};";
            }).ToArray());
        }
    }
#pragma warning restore CA1822 // Method should be static
    private static bool ShouldTypeHaveNullableMarker(CodeTypeBase propType, string propTypeName)
    {
        return propType.IsNullable && (NullableTypes.Contains(propTypeName) || (propType is CodeType codeType && codeType.TypeDefinition is CodeEnum));
    }
    private HashSet<string> _namespaceSegmentsNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _namespaceSegmentsNamesLock = new();
    private HashSet<string> GetNamesInUseByNamespaceSegments(CodeElement currentElement)
    {
        if (_namespaceSegmentsNames.Count == 0)
        {
            lock (_namespaceSegmentsNamesLock)
            {
                var rootNamespace = currentElement.GetImmediateParentOfType<CodeNamespace>().GetRootNamespace();
                _namespaceSegmentsNames = GetAllNamespaces(rootNamespace)
                                            .Where(static x => !string.IsNullOrEmpty(x.Name))
                                            .SelectMany(static ns => ns.Name.Split('.', StringSplitOptions.RemoveEmptyEntries))
                                            .Distinct(StringComparer.OrdinalIgnoreCase)
                                            .ToHashSet(StringComparer.OrdinalIgnoreCase);
                _namespaceSegmentsNames.Add("keyvaluepair"); //workaround as System.Collections.Generic imports keyvalue pair
            }
        }
        return _namespaceSegmentsNames;
    }
    private static IEnumerable<CodeNamespace> GetAllNamespaces(CodeNamespace ns)
    {
        foreach (var childNs in ns.Namespaces)
        {
            yield return childNs;
            foreach (var childNsSegment in GetAllNamespaces(childNs))
                yield return childNsSegment;
        }
    }
    public override string GetTypeString(CodeTypeBase code, CodeElement targetElement, bool includeCollectionInformation = true, LanguageWriter? writer = null)
    {
        return GetTypeString(code, targetElement, includeCollectionInformation, true);
    }
    public string GetTypeString(CodeTypeBase code, CodeElement targetElement, bool includeCollectionInformation, bool includeNullableInformation, bool includeActionInformation = true)
    {
        ArgumentNullException.ThrowIfNull(targetElement);
        if (code is CodeComposedTypeBase)
            throw new InvalidOperationException($"Dart does not support union types, the union type {code.Name} should have been filtered out by the refiner");
        if (code is CodeType currentType)
        {
            var typeName = TranslateTypeAndAvoidUsingNamespaceSegmentNames(currentType, targetElement);
            var alias = GetTypeAlias(currentType, targetElement);
            if (!string.IsNullOrEmpty(alias))
            {
                typeName = alias + "." + typeName;
            }
            var nullableSuffix = ShouldTypeHaveNullableMarker(code, typeName) && includeNullableInformation ? NullableMarkerAsString : string.Empty;
            var collectionPrefix = currentType.CollectionKind == CodeTypeCollectionKind.Complex && includeCollectionInformation ? "Iterable<" : string.Empty;
            if (currentType.CollectionKind == CodeTypeCollectionKind.Array && includeCollectionInformation)
            {
                collectionPrefix = "List<";
            }
            var collectionSuffix = currentType.CollectionKind == CodeTypeCollectionKind.None || !includeCollectionInformation ? string.Empty : ">";
            var genericParameters = currentType.GenericTypeParameterValues.Count != 0 ?
                $"<{string.Join(", ", currentType.GenericTypeParameterValues.Select(x => GetTypeString(x, targetElement, includeCollectionInformation)))}>" :
                string.Empty;
            if (currentType.ActionOf && includeActionInformation)
            {
                return $"Function({collectionPrefix}{typeName}{genericParameters}{nullableSuffix}{collectionSuffix})";
            }

            return $"{collectionPrefix}{typeName}{genericParameters}{collectionSuffix}{nullableSuffix}";
        }

        throw new InvalidOperationException($"type of type {code?.GetType()} is unknown");
    }

    private static string GetTypeAlias(CodeType targetType, CodeElement targetElement)
    {
        if (targetElement.GetImmediateParentOfType<IBlock>() is IBlock parentBlock &&
            parentBlock.Usings
                        .FirstOrDefault(x => !x.IsExternal &&
                                        x.Declaration?.TypeDefinition != null &&
                                        x.Declaration.TypeDefinition == targetType.TypeDefinition &&
                                        !string.IsNullOrEmpty(x.Alias)) is CodeUsing aliasedUsing)
            return aliasedUsing.Alias;
        return string.Empty;
    }
    private string TranslateTypeAndAvoidUsingNamespaceSegmentNames(CodeType currentType, CodeElement targetElement)
    {
        var parentElementsHash = targetElement.Parent is CodeClass parentClass ?
            parentClass.Methods.Select(static x => x.Name)
                .Union(parentClass.Properties.Select(static x => x.Name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase) :
            new HashSet<string>(0, StringComparer.OrdinalIgnoreCase);

        var typeName = TranslateType(currentType);
        return typeName;
    }

    private static bool DoesTypeExistsInSameNamesSpaceAsTarget(CodeType currentType, CodeElement targetElement)
    {
        return currentType?.TypeDefinition?.GetImmediateParentOfType<CodeNamespace>()?.Name.Equals(targetElement?.GetImmediateParentOfType<CodeNamespace>()?.Name, StringComparison.OrdinalIgnoreCase) ?? false;
    }

    private static bool DoesTypeExistsInTargetAncestorNamespace(CodeType currentType, CodeElement targetElement)
    {
        // Avoid type ambiguity on similarly named classes. Currently, if we have namespaces A and A.B where both namespaces have type T,
        // Trying to use type A.B.T in namespace A without using a qualified name will break the build.
        // Similarly, if we have type A.B.C.D.T1 that needs to be used within type A.B.C.T2, but there's also a type
        // A.B.T1, using T1 in T2 will resolve A.B.T1 even if you have a using statement with A.B.C.D.
        var hasChildWithName = false;
        if (currentType != null && currentType.TypeDefinition != null && !currentType.IsExternal && targetElement != null)
        {
            var typeName = currentType.TypeDefinition.Name;
            var ns = targetElement.GetImmediateParentOfType<CodeNamespace>();
            var rootNs = ns?.GetRootNamespace();
            while (ns is not null && ns != rootNs && !hasChildWithName)
            {
                hasChildWithName = ns.GetChildElements(true).OfType<CodeClass>().Any(c => c.Name?.Equals(typeName, StringComparison.OrdinalIgnoreCase) == true);
                ns = ns.Parent is CodeNamespace n ? n : (ns.GetImmediateParentOfType<CodeNamespace>());
            }
        }
        return hasChildWithName;
    }

    private static bool DoesTypeExistsInOtherImportedNamespaces(CodeType currentType, CodeElement targetElement)
    {
        if (currentType.TypeDefinition is CodeClass { Parent: CodeNamespace currentTypeNamespace } codeClass)
        {
            var targetClass = targetElement.GetImmediateParentOfType<CodeClass>();
            var importedNamespaces = targetClass.StartBlock.Usings
                .Where(codeUsing => !codeUsing.IsExternal // 1. Are defined during generation(not external) 
                                    && codeUsing.Declaration?.TypeDefinition != null
                                    && !codeUsing.Name.Equals(currentTypeNamespace.Name, StringComparison.OrdinalIgnoreCase))  // 2. Do not match the namespace of the current type
                .Select(static codeUsing => codeUsing.Declaration!.TypeDefinition!.GetImmediateParentOfType<CodeNamespace>())
                .DistinctBy(static declaredNamespace => declaredNamespace.Name);

            return importedNamespaces.Any(importedNamespace => (importedNamespace.FindChildByName<CodeClass>(codeClass.Name, false) != null)
                                                               || (importedNamespace.FindChildByName<CodeEnum>(codeClass.Name, false) != null));
        }
        return false;
    }

    public override string TranslateType(CodeType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.Name.ToLowerInvariant() switch
        {
            "integer" or "sbyte" or "byte" or "int64" => "int",
            "boolean" => "bool",
            "string" => "String",
            "double" or "float" or "decimal" => "double",
            "object" or "void" => type.Name.ToLowerInvariant(),// little casing hack
            "binary" or "base64" or "base64url" => "Iterable<int>",
            "datetimeoffset" => "DateTime",
            string s when s.Contains("RequestConfiguration", StringComparison.OrdinalIgnoreCase) => "RequestConfiguration",
            "iparsenode" => "ParseNode",
            "iserializationwriter" => "SerializationWriter",
            _ => type.Name.ToFirstCharacterUpperCase() is string typeName && !string.IsNullOrEmpty(typeName) ? typeName : "Object",
        };
    }

    public bool IsPrimitiveType(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return false;
        typeName = typeName.StripArraySuffix().TrimEnd('?').ToLowerInvariant();
        return typeName switch
        {
            "string" or "dateonly" or "timeonly" or "datetime" or "duration" => true,
            _ when NullableTypes.Contains(typeName) => true,
            _ => false,
        };
    }
    public override string GetParameterSignature(CodeParameter parameter, CodeElement targetElement, LanguageWriter? writer = null)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        var parameterType = GetTypeString(parameter.Type, targetElement, true, parameter.Optional);
        var defaultValue = parameter switch
        {
            _ when !string.IsNullOrEmpty(parameter.DefaultValue) => $" = {parameter.DefaultValue}",
            _ when nameof(String).Equals(parameterType, StringComparison.OrdinalIgnoreCase) && parameter.Optional => " = \"\"",
            _ => string.Empty,
        };
        var open = !string.IsNullOrEmpty(defaultValue) ? "{" : "";
        var close = !string.IsNullOrEmpty(defaultValue) ? "}" : "";
        return $"{GetDeprecationInformation(parameter)}{open}{parameterType} {parameter.Name.ToFirstCharacterLowerCase()}{defaultValue}{close}";
    }
    private string GetDeprecationInformation(IDeprecableElement element)
    {
        if (element.Deprecation is null || !element.Deprecation.IsDeprecated) return string.Empty;

        var versionComment = string.IsNullOrEmpty(element.Deprecation.Version) ? string.Empty : $" as of {element.Deprecation.Version}";
        var dateComment = element.Deprecation.Date is null ? string.Empty : $" on {element.Deprecation.Date.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
        var removalComment = element.Deprecation.RemovalDate is null ? string.Empty : $" and will be removed {element.Deprecation.RemovalDate.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
        return $"@Deprecated(\"{element.Deprecation.GetDescription(type => GetTypeString(type, (element as CodeElement)!))}{versionComment}{dateComment}{removalComment}\")";
    }
    internal void WriteDeprecationAttribute(IDeprecableElement element, LanguageWriter writer)
    {
        var deprecationMessage = GetDeprecationInformation(element);
        if (!string.IsNullOrEmpty(deprecationMessage))
            writer.WriteLine(deprecationMessage);
    }

    public bool ErrorClassPropertyExistsInSuperClass(CodeProperty codeElement)
    {
        return codeElement?.Parent is CodeClass parentClass && parentClass.IsErrorDefinition && ErrorClassProperties.Contains(codeElement.Name);
    }

    public string getCorrectedEnumName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Contains('_', StringComparison.Ordinal))
            return name.ToLowerInvariant().ToCamelCase('_');
        return name.All(c => char.IsUpper(c) || char.IsAsciiDigit(c)) ? name.ToLowerInvariant() : name;
    }
}