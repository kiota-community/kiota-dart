﻿using System;
using System.Linq;
using Kiota.Builder.CodeDOM;
using Kiota.Builder.Extensions;
using Kiota.Builder.Refiners;

namespace Kiota.Builder.Writers.Dart;
public class CodePropertyWriter : BaseElementWriter<CodeProperty, DartConventionService>
{
    private DartReservedNamesProvider reservedNamesProvider = new DartReservedNamesProvider();
    public CodePropertyWriter(DartConventionService conventionService) : base(conventionService) { }
    public override void WriteCodeElement(CodeProperty codeElement, LanguageWriter writer)
    {
        ArgumentNullException.ThrowIfNull(codeElement);
        ArgumentNullException.ThrowIfNull(writer);
        if (codeElement.ExistsInExternalBaseType || conventions.ErrorClassPropertyExistsInSuperClass(codeElement)) return;
        var propertyType = conventions.GetTypeString(codeElement.Type, codeElement);
        var isNullableReferenceType = !propertyType.EndsWith('?')
                                      && codeElement.IsOfKind(
                                            CodePropertyKind.Custom,
                                            CodePropertyKind.QueryParameter, CodePropertyKind.ErrorMessageOverride);
        conventions.WriteShortDescription(codeElement, writer);
        conventions.WriteDeprecationAttribute(codeElement, writer);
        if (isNullableReferenceType)
        {
            WritePropertyInternal(codeElement, writer, $"{propertyType}?");
        }
        else
        {
            WritePropertyInternal(codeElement, writer, propertyType);
        }
    }

    private void WritePropertyInternal(CodeProperty codeElement, LanguageWriter writer, string propertyType)
    {
        if (codeElement.Parent is not CodeClass parentClass)
            throw new InvalidOperationException("The parent of a property should be a class");

        var backingStoreProperty = parentClass.GetBackingStoreProperty();
        var defaultValue = string.Empty;
        var getterModifier = string.Empty;

        var accessModifierAttribute = conventions.GetAccessModifierAttribute(codeElement.Access);
        if (!string.IsNullOrEmpty(accessModifierAttribute))
            writer.WriteLine(accessModifierAttribute);

        var propertyName = codeElement.Name.ToCamelCase();
        if (reservedNamesProvider.ReservedNames.Contains(propertyName))
        {
            propertyName += "Escaped";
        }
        switch (codeElement.Kind)
        {
            case CodePropertyKind.RequestBuilder:
                writer.WriteLine($"{propertyType} get {conventions.GetAccessModifierPrefix(codeElement.Access)}{propertyName} {{");
                writer.IncreaseIndent();
                conventions.AddRequestBuilderBody(parentClass, propertyType, writer, prefix: "return ");
                writer.DecreaseIndent();
                writer.WriteLine("}");
                break;
            case CodePropertyKind.AdditionalData when backingStoreProperty != null:
            case CodePropertyKind.Custom when backingStoreProperty != null:
                var backingStoreKey = codeElement.WireName;
                var defaultIfNotNullable = propertyType.EndsWith('?') ? string.Empty : $" ?? {codeElement.DefaultValue}";
                writer.WriteLine($"{propertyType} get {conventions.GetAccessModifierPrefix(codeElement.Access)}{propertyName} {{");
                writer.IncreaseIndent();
                writer.WriteLine($"return {backingStoreProperty.Name.ToFirstCharacterLowerCase()}.get<{propertyType}>('{backingStoreKey}'){defaultIfNotNullable};");
                writer.DecreaseIndent();
                writer.WriteLine("}");
                writer.WriteLine();
                writer.WriteLine($"set {codeElement.Name.ToCamelCase()}({propertyType} value) {{");
                writer.IncreaseIndent();
                writer.WriteLine($"{backingStoreProperty.Name.ToFirstCharacterLowerCase()}.set('{backingStoreKey}', value);");
                writer.DecreaseIndent();
                writer.WriteLine("}");
                break;
            case CodePropertyKind.ErrorMessageOverride when parentClass.IsErrorDefinition:
                writer.WriteLine("@override");
                goto default;
            case CodePropertyKind.QueryParameter when codeElement.IsNameEscaped:
                writer.WriteLine($"/// @QueryParameter('{codeElement.SerializationName}')");
                goto default;
            case CodePropertyKind.QueryParameters:
                defaultValue = $" = {propertyType}()";
                goto default;
            case CodePropertyKind.AdditionalData:
                if (parentClass.StartBlock.Implements.Where(static x => x.Name.Equals("AdditionalDataHolder", StringComparison.Ordinal)).Any())
                    writer.WriteLine("@override");
                goto default;
            case CodePropertyKind.BackingStore:
                defaultValue = " = BackingStoreFactorySingleton.instance.createBackingStore()";
                goto default;
            default:
                writer.WriteLine($"{propertyType} {getterModifier}{conventions.GetAccessModifierPrefix(codeElement.Access)}{codeElement.Name.ToFirstCharacterLowerCase()}{defaultValue};");
                break;
        }
    }
}