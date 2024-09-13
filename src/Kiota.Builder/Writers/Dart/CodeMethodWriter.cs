﻿using System;
using System.Collections.Generic;
using System.Linq;
using Kiota.Builder.CodeDOM;
using Kiota.Builder.Extensions;
using Kiota.Builder.OrderComparers;
using Microsoft.OpenApi.Extensions;

namespace Kiota.Builder.Writers.Dart;
public class CodeMethodWriter : BaseElementWriter<CodeMethod, DartConventionService>
{
    public CodeMethodWriter(DartConventionService conventionService) : base(conventionService)
    {

    }
    public override void WriteCodeElement(CodeMethod codeElement, LanguageWriter writer)
    {
        ArgumentNullException.ThrowIfNull(codeElement);
        if (codeElement.ReturnType == null) throw new InvalidOperationException($"{nameof(codeElement.ReturnType)} should not be null");
        ArgumentNullException.ThrowIfNull(writer);
        if (codeElement.Parent is not CodeClass parentClass) throw new InvalidOperationException("the parent of a method should be a class");

        var returnType = conventions.GetTypeString(codeElement.ReturnType, codeElement);
        var inherits = parentClass.StartBlock.Inherits != null && !parentClass.IsErrorDefinition;
        var isVoid = conventions.VoidTypeName.Equals(returnType, StringComparison.OrdinalIgnoreCase);
        WriteMethodDocumentation(codeElement, writer);
        WriteMethodPrototype(codeElement, parentClass, writer, returnType, inherits, isVoid);
        writer.IncreaseIndent();
        foreach (var parameter in codeElement.Parameters.Where(static x => !x.Optional && !x.IsOfKind(CodeParameterKind.RequestAdapter, CodeParameterKind.PathParameters, CodeParameterKind.RawUrl)).OrderBy(static x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            var parameterName = parameter.Name.ToFirstCharacterLowerCase();
            if (nameof(String).Equals(parameter.Type.Name, StringComparison.OrdinalIgnoreCase) && parameter.Type.CollectionKind == CodeTypeBase.CodeTypeCollectionKind.None)
                writer.WriteLine($"if({parameterName} != null || {parameterName}.isEmpty) throw ArgumentError.notNull({parameterName});");
        }
        HandleMethodKind(codeElement, writer, inherits, parentClass, isVoid);

        var isConstructor = codeElement.IsOfKind(CodeMethodKind.Constructor, CodeMethodKind.ClientConstructor, CodeMethodKind.RawUrlConstructor);
        if (isConstructor && !inherits && parentClass.Properties.Where(x => x.IsOfKind(CodePropertyKind.AdditionalData)).Any())
        {
            writer.DecreaseIndent();
        }
        else
        {
            writer.CloseBlock();
        }
    }

    protected virtual void HandleMethodKind(CodeMethod codeElement, LanguageWriter writer, bool doesInherit, CodeClass parentClass, bool isVoid)
    {
        ArgumentNullException.ThrowIfNull(codeElement);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(parentClass);
        var returnType = conventions.GetTypeString(codeElement.ReturnType, codeElement);
        var returnTypeWithoutCollectionInformation = conventions.GetTypeString(codeElement.ReturnType, codeElement, false);
        var requestBodyParam = codeElement.Parameters.OfKind(CodeParameterKind.RequestBody);
        var requestConfig = codeElement.Parameters.OfKind(CodeParameterKind.RequestConfiguration);
        var requestContentType = codeElement.Parameters.OfKind(CodeParameterKind.RequestBodyContentType);
        var requestParams = new RequestParams(requestBodyParam, requestConfig, requestContentType);
        switch (codeElement.Kind)
        {
            case CodeMethodKind.Serializer:
                WriteSerializerBody(doesInherit, codeElement, parentClass, writer);
                break;
            case CodeMethodKind.RequestGenerator:
                WriteRequestGeneratorBody(codeElement, requestParams, parentClass, writer);
                break;
            case CodeMethodKind.RequestExecutor:
                WriteRequestExecutorBody(codeElement, requestParams, parentClass, isVoid, returnTypeWithoutCollectionInformation, writer);
                break;
            case CodeMethodKind.Deserializer:
                WriteDeserializerBody(doesInherit, codeElement, parentClass, writer);
                break;
            case CodeMethodKind.ClientConstructor:
                WriteConstructorBody(parentClass, codeElement, writer);
                WriteApiConstructorBody(parentClass, codeElement, writer);
                break;
            case CodeMethodKind.RawUrlBuilder:
                WriteRawUrlBuilderBody(parentClass, codeElement, writer);
                break;
            case CodeMethodKind.Constructor:
            case CodeMethodKind.RawUrlConstructor:
                WriteConstructorBody(parentClass, codeElement, writer);
                break;
            case CodeMethodKind.RequestBuilderWithParameters:
                WriteRequestBuilderBody(parentClass, codeElement, writer);
                break;
            case CodeMethodKind.Getter:
            case CodeMethodKind.Setter:
                throw new InvalidOperationException("getters and setters are automatically added on fields in dotnet");
            case CodeMethodKind.RequestBuilderBackwardCompatibility:
                throw new InvalidOperationException("RequestBuilderBackwardCompatibility is not supported as the request builders are implemented by properties.");
            case CodeMethodKind.ErrorMessageOverride:
                throw new InvalidOperationException("ErrorMessageOverride is not supported as the error message is implemented by a property.");
            case CodeMethodKind.CommandBuilder:
                var origParams = codeElement.OriginalMethod?.Parameters ?? codeElement.Parameters;
                requestBodyParam = origParams.OfKind(CodeParameterKind.RequestBody);
                requestConfig = origParams.OfKind(CodeParameterKind.RequestConfiguration);
                requestContentType = origParams.OfKind(CodeParameterKind.RequestBodyContentType);
                requestParams = new RequestParams(requestBodyParam, requestConfig, requestContentType);
                WriteCommandBuilderBody(codeElement, parentClass, requestParams, isVoid, returnType, writer);
                break;
            case CodeMethodKind.Factory:
                WriteFactoryMethodBody(codeElement, parentClass, writer);
                break;
            case CodeMethodKind.ComposedTypeMarker:
                throw new InvalidOperationException("ComposedTypeMarker is not required as interface is explicitly implemented.");
            default:
                writer.WriteLine("return null;");
                break;
        }
    }
    private void WriteRawUrlBuilderBody(CodeClass parentClass, CodeMethod codeElement, LanguageWriter writer)
    {
        var rawUrlParameter = codeElement.Parameters.OfKind(CodeParameterKind.RawUrl) ?? throw new InvalidOperationException("RawUrlBuilder method should have a RawUrl parameter");
        var requestAdapterProperty = parentClass.GetPropertyOfKind(CodePropertyKind.RequestAdapter) ?? throw new InvalidOperationException("RawUrlBuilder method should have a RequestAdapter property");
        writer.WriteLine($"return {parentClass.Name.ToFirstCharacterUpperCase()}.withUrl({rawUrlParameter.Name.ToFirstCharacterLowerCase()}, {requestAdapterProperty.Name.ToFirstCharacterLowerCase()});");
    }
    private static readonly CodePropertyTypeComparer CodePropertyTypeForwardComparer = new();
    private static readonly CodePropertyTypeComparer CodePropertyTypeBackwardComparer = new(true);
    private void WriteFactoryMethodBodyForInheritedModel(CodeMethod codeElement, CodeClass parentClass, LanguageWriter writer)
    {
        writer.StartBlock($"return {DiscriminatorMappingVarName} switch {{");
        foreach (var mappedType in parentClass.DiscriminatorInformation.DiscriminatorMappings)
        {
            writer.WriteLine($"\"{mappedType.Key}\" => {conventions.GetTypeString(mappedType.Value.AllTypes.First(), codeElement)}(),");
        }
        writer.WriteLine($"_ => {parentClass.Name.ToFirstCharacterUpperCase()}(),");
        writer.CloseBlock("};");
    }
    private const string ResultVarName = "result";
    private void WriteFactoryMethodBodyForUnionModel(CodeMethod codeElement, CodeClass parentClass, CodeParameter parseNodeParameter, LanguageWriter writer)
    {
        writer.WriteLine($"var {ResultVarName} = {parentClass.Name.ToFirstCharacterUpperCase()}();");
        var includeElse = false;
        foreach (var property in parentClass.GetPropertiesOfKind(CodePropertyKind.Custom)
                                            .OrderBy(static x => x, CodePropertyTypeForwardComparer)
                                            .ThenBy(static x => x.Name, StringComparer.Ordinal))
        {
            if (property.Type is CodeType propertyType)
                if (propertyType.TypeDefinition is CodeClass && !propertyType.IsCollection)
                {
                    var mappedType = parentClass.DiscriminatorInformation.DiscriminatorMappings.FirstOrDefault(x => x.Value.Name.Equals(propertyType.Name, StringComparison.OrdinalIgnoreCase));
                    writer.WriteLine($"{(includeElse ? "else " : string.Empty)}if(\"{mappedType.Key}.toUpperCase()\".equals({DiscriminatorMappingVarName}.toUpperCase())) {{");
                    writer.IncreaseIndent();
                    writer.WriteLine($"{ResultVarName}.{property.Name.ToFirstCharacterLowerCase()} = {conventions.GetTypeString(propertyType, codeElement)}();");
                    writer.CloseBlock();
                }
                else if (propertyType.TypeDefinition is CodeClass && propertyType.IsCollection || propertyType.TypeDefinition is null || propertyType.TypeDefinition is CodeEnum)
                {
                    var typeName = conventions.GetTypeString(propertyType, codeElement, true, false);
                    var valueVarName = $"{property.Name.ToFirstCharacterLowerCase()}Value";
                    writer.WriteLine($"{(includeElse ? "else " : string.Empty)}if({parseNodeParameter.Name.ToFirstCharacterLowerCase()}.{GetDeserializationMethodName(propertyType, codeElement)} is {typeName} {valueVarName}) {{");
                    writer.IncreaseIndent();
                    writer.WriteLine($"{ResultVarName}.{property.Name.ToFirstCharacterUpperCase()} = {valueVarName};");
                    writer.CloseBlock();
                }
            if (!includeElse)
                includeElse = true;
        }
        writer.WriteLine($"return {ResultVarName};");
    }
    private void WriteFactoryMethodBodyForIntersectionModel(CodeMethod codeElement, CodeClass parentClass, CodeParameter parseNodeParameter, LanguageWriter writer)
    {
        writer.WriteLine($"var {ResultVarName} = {parentClass.Name.ToFirstCharacterUpperCase()}();");
        var includeElse = false;
        foreach (var property in parentClass.GetPropertiesOfKind(CodePropertyKind.Custom)
                                            .Where(static x => x.Type is not CodeType propertyType || propertyType.IsCollection || propertyType.TypeDefinition is not CodeClass)
                                            .OrderBy(static x => x, CodePropertyTypeBackwardComparer)
                                            .ThenBy(static x => x.Name, StringComparer.Ordinal))
        {
            if (property.Type is CodeType propertyType)
            {
                var typeName = conventions.GetTypeString(propertyType, codeElement, true, false);
                var valueVarName = $"{property.Name.ToFirstCharacterLowerCase()}Value";
                writer.StartBlock($"{(includeElse ? "else " : string.Empty)}if({parseNodeParameter.Name.ToFirstCharacterLowerCase()}.{GetDeserializationMethodName(propertyType, codeElement)} is {typeName} {valueVarName}) {{");
                writer.WriteLine($"{ResultVarName}.{property.Name.ToFirstCharacterUpperCase()} = {valueVarName};");
                writer.CloseBlock();
            }
            if (!includeElse)
                includeElse = true;
        }
        var complexProperties = parentClass.GetPropertiesOfKind(CodePropertyKind.Custom)
                                            .Where(static x => x.Type is CodeType xType && xType.TypeDefinition is CodeClass && !xType.IsCollection)
                                            .Select(static x => new Tuple<CodeProperty, CodeType>(x, (CodeType)x.Type))
                                            .ToArray();
        if (complexProperties.Length != 0)
        {
            if (includeElse)
            {
                writer.WriteLine("else {");
                writer.IncreaseIndent();
            }
            foreach (var property in complexProperties)
                writer.WriteLine($"{ResultVarName}.{property.Item1.Name.ToFirstCharacterUpperCase()} = {conventions.GetTypeString(property.Item2, codeElement)}();");
            if (includeElse)
            {
                writer.CloseBlock();
            }
        }
        writer.WriteLine($"return {ResultVarName};");
    }
    private const string DiscriminatorMappingVarName = "mappingValue";
    private void WriteFactoryMethodBody(CodeMethod codeElement, CodeClass parentClass, LanguageWriter writer)
    {
        var parseNodeParameter = codeElement.Parameters.OfKind(CodeParameterKind.ParseNode) ?? throw new InvalidOperationException("Factory method should have a ParseNode parameter");

        if (parentClass.DiscriminatorInformation.ShouldWriteParseNodeCheck && !parentClass.DiscriminatorInformation.ShouldWriteDiscriminatorForIntersectionType)
            writer.WriteLine($"var {DiscriminatorMappingVarName} = {parseNodeParameter.Name.ToFirstCharacterLowerCase()}.GetChildNode(\"{parentClass.DiscriminatorInformation.DiscriminatorPropertyName}\")?.GetStringValue();");

        if (parentClass.DiscriminatorInformation.ShouldWriteDiscriminatorForInheritedType)
            WriteFactoryMethodBodyForInheritedModel(codeElement, parentClass, writer);
        else if (parentClass.DiscriminatorInformation.ShouldWriteDiscriminatorForUnionType)
            WriteFactoryMethodBodyForUnionModel(codeElement, parentClass, parseNodeParameter, writer);
        else if (parentClass.DiscriminatorInformation.ShouldWriteDiscriminatorForIntersectionType)
            WriteFactoryMethodBodyForIntersectionModel(codeElement, parentClass, parseNodeParameter, writer);
        else
            writer.WriteLine($"return {parentClass.Name.ToFirstCharacterUpperCase()}();");
    }
    private void WriteRequestBuilderBody(CodeClass parentClass, CodeMethod codeElement, LanguageWriter writer)
    {
        var importSymbol = conventions.GetTypeString(codeElement.ReturnType, parentClass);
        conventions.AddRequestBuilderBody(parentClass, importSymbol, writer, prefix: "return ", pathParameters: codeElement.Parameters.Where(static x => x.IsOfKind(CodeParameterKind.Path)));
    }
    private static void WriteApiConstructorBody(CodeClass parentClass, CodeMethod method, LanguageWriter writer)
    {
        if (parentClass.GetPropertyOfKind(CodePropertyKind.RequestAdapter) is not CodeProperty requestAdapterProperty) return;
        var pathParametersProperty = parentClass.GetPropertyOfKind(CodePropertyKind.PathParameters);
        var backingStoreParameter = method.Parameters.OfKind(CodeParameterKind.BackingStore);
        var requestAdapterPropertyName = requestAdapterProperty.Name.ToFirstCharacterLowerCase();
        WriteSerializationRegistration(method.SerializerModules, writer, "registerDefaultSerializer");
        WriteSerializationRegistration(method.DeserializerModules, writer, "registerDefaultDeserializer");
        if (!string.IsNullOrEmpty(method.BaseUrl))
        {
            writer.StartBlock($"if ({requestAdapterPropertyName}.baseUrl != null || {requestAdapterPropertyName}.baseUrl!.isEmpty) {{");
            writer.WriteLine($"{requestAdapterPropertyName}.baseUrl = \"{method.BaseUrl}\";");
            writer.CloseBlock();
            if (pathParametersProperty != null)
                writer.WriteLine($"{pathParametersProperty.Name.ToFirstCharacterLowerCase()}[\"baseurl\"] = {requestAdapterPropertyName}.baseUrl;");
        }
        if (backingStoreParameter != null)
            writer.WriteLine($"{requestAdapterPropertyName}.EnableBackingStore({backingStoreParameter.Name});");
    }
    private static void WriteSerializationRegistration(HashSet<string> serializationClassNames, LanguageWriter writer, string methodName)
    {
        if (serializationClassNames != null)
            foreach (var serializationClassName in serializationClassNames)
                writer.WriteLine($"ApiClientBuilder.{methodName}(() => {serializationClassName}());");
    }
    private void WriteConstructorBody(CodeClass parentClass, CodeMethod currentMethod, LanguageWriter writer)
    {
        foreach (var propWithDefault in parentClass
                                        .Properties
                                        .Where(static x => !string.IsNullOrEmpty(x.DefaultValue) && !x.IsOfKind(CodePropertyKind.UrlTemplate, CodePropertyKind.PathParameters))
                                        // do not apply the default value if the type is composed as the default value may not necessarily which type to use
                                        .Where(static x => x.Type is not CodeType propType || propType.TypeDefinition is not CodeClass propertyClass || propertyClass.OriginalComposedType is null)
                                        .OrderByDescending(static x => x.Kind)
                                        .ThenBy(static x => x.Name))
        {
            var defaultValue = propWithDefault.DefaultValue;
            if (propWithDefault.Type is CodeType propertyType && propertyType.TypeDefinition is CodeEnum)
            {
                defaultValue = $"{conventions.GetTypeString(propWithDefault.Type, currentMethod).TrimEnd('?')}.{defaultValue.Trim('"').CleanupSymbolName().ToFirstCharacterUpperCase()}";
            }
            writer.WriteLine($"{propWithDefault.Name.ToFirstCharacterLowerCase()} = {defaultValue};");
        }
        if (parentClass.IsOfKind(CodeClassKind.RequestBuilder) &&
            parentClass.GetPropertyOfKind(CodePropertyKind.PathParameters) is CodeProperty pathParametersProp &&
            currentMethod.IsOfKind(CodeMethodKind.Constructor) &&
            currentMethod.Parameters.OfKind(CodeParameterKind.PathParameters) is CodeParameter pathParametersParam)
        {
            var pathParameters = currentMethod.Parameters.Where(static x => x.IsOfKind(CodeParameterKind.Path));
            if (pathParameters.Any())
                conventions.AddParametersAssignment(writer,
                                                    pathParametersParam.Type,
                                                    pathParametersParam.Name.ToFirstCharacterLowerCase(),
                                                    pathParametersProp.Name.ToFirstCharacterUpperCase(),
                                                    currentMethod.Parameters
                                                                .Where(static x => x.IsOfKind(CodeParameterKind.Path))
                                                                .Select(static x => (x.Type, string.IsNullOrEmpty(x.SerializationName) ? x.Name : x.SerializationName, x.Name.ToFirstCharacterLowerCase()))
                                                                .ToArray());
        }
    }
    private string DefaultDeserializerValue => $"Map<String, Function({conventions.ParseNodeInterfaceName})>";
    private void WriteDeserializerBody(bool shouldHide, CodeMethod codeElement, CodeClass parentClass, LanguageWriter writer)
    {
        if (parentClass.DiscriminatorInformation.ShouldWriteDiscriminatorForUnionType)
            WriteDeserializerBodyForUnionModel(codeElement, parentClass, writer);
        else if (parentClass.DiscriminatorInformation.ShouldWriteDiscriminatorForIntersectionType)
            WriteDeserializerBodyForIntersectionModel(parentClass, writer);
        else
            WriteDeserializerBodyForInheritedModel(shouldHide, codeElement, parentClass, writer);
    }
    private void WriteDeserializerBodyForUnionModel(CodeMethod method, CodeClass parentClass, LanguageWriter writer)
    {
        var includeElse = false;
        foreach (var otherPropName in parentClass
                                        .GetPropertiesOfKind(CodePropertyKind.Custom)
                                        .Where(static x => !x.ExistsInBaseType)
                                        .Where(static x => x.Type is CodeType propertyType && !propertyType.IsCollection && propertyType.TypeDefinition is CodeClass)
                                        .OrderBy(static x => x, CodePropertyTypeForwardComparer)
                                        .ThenBy(static x => x.Name)
                                        .Select(static x => x.Name.ToFirstCharacterUpperCase()))
        {
            writer.StartBlock($"{(includeElse ? "else " : string.Empty)}if({otherPropName} != null) {{");
            writer.WriteLine($"return {otherPropName}.{method.Name.ToFirstCharacterUpperCase()}();");
            writer.CloseBlock();
            if (!includeElse)
                includeElse = true;
        }
        writer.WriteLine($"return {DefaultDeserializerValue}();");
    }
    private const string DeserializerReturnType = "Map<String, Function(ParseNode)>";
    private void WriteDeserializerBodyForIntersectionModel(CodeClass parentClass, LanguageWriter writer)
    {
        var complexProperties = parentClass.GetPropertiesOfKind(CodePropertyKind.Custom)
                                            .Where(static x => x.Type is CodeType propType && propType.TypeDefinition is CodeClass && !x.Type.IsCollection)
                                            .ToArray();
        if (complexProperties.Length != 0)
        {
            var propertiesNames = complexProperties
                                .Select(static x => x.Name.ToFirstCharacterUpperCase())
                                .OrderBy(static x => x)
                                .ToArray();
            var propertiesNamesAsConditions = propertiesNames
                                .Select(static x => $"{x} != null")
                                .Aggregate(static (x, y) => $"{x} || {y}");
            writer.StartBlock($"if({propertiesNamesAsConditions}) {{");
            var propertiesNamesAsArgument = propertiesNames
                                .Aggregate(static (x, y) => $"{x}, {y}");
            writer.WriteLine($"return ParseNodeHelper.MergeDeserializersForIntersectionWrapper({propertiesNamesAsArgument});");
            writer.CloseBlock();
        }
        writer.WriteLine($"return {DefaultDeserializerValue}();");
    }
    private const string DeserializerVarName = "deserializerMap";
    private void WriteDeserializerBodyForInheritedModel(bool shouldHide, CodeMethod codeElement, CodeClass parentClass, LanguageWriter writer)
    {

        var fieldToSerialize = parentClass.GetPropertiesOfKind(CodePropertyKind.Custom).ToArray();
        writer.WriteLine($"{DeserializerReturnType} {DeserializerVarName} = " + (shouldHide ? "super.getFieldDeserializers();" : "{};"));

        if (fieldToSerialize.Length != 0)
        {
            fieldToSerialize
                    .Where(static x => !x.ExistsInBaseType)
                    .OrderBy(static x => x.Name)
                    .Select(x =>
                        $"{DeserializerVarName}['{x.WireName}'] = (node) => {x.WireName} = node.{GetDeserializationMethodName(x.Type, codeElement)};")
                    .ToList()
                    .ForEach(x => writer.WriteLine(x));
        }
        writer.WriteLine($"return {DeserializerVarName};");
    }
    private string GetDeserializationMethodName(CodeTypeBase propType, CodeMethod method)
    {
        var isCollection = propType.CollectionKind != CodeTypeBase.CodeTypeCollectionKind.None;
        var propertyType = conventions.GetTypeString(propType, method, false);
        if (propType is CodeType currentType)
        {
            if (isCollection)
            {
                var collectionMethod = "";
                if (currentType.TypeDefinition == null)
                    return $"getCollectionOfPrimitiveValues<{propertyType}>(){collectionMethod}";
                else if (currentType.TypeDefinition is CodeEnum)
                    return $"getCollectionOfEnumValues<{propertyType.TrimEnd('?')}>(){collectionMethod}";
                else
                    return $"getCollectionOfObjectValues<{propertyType}>({propertyType}.CreateFromDiscriminatorValue){collectionMethod}";
            }
            else if (currentType.TypeDefinition is CodeEnum enumType)
                return $"getEnumValue<{enumType.Name.ToFirstCharacterUpperCase()}>()";
        }
        return propertyType switch
        {
            "byte[]" => "getByteArrayValue()",
            String => "getStringValue()",
            _ when conventions.IsPrimitiveType(propertyType) => $"get{propertyType.TrimEnd(DartConventionService.NullableMarker).ToFirstCharacterUpperCase()}Value()",
            _ => $"getObjectValue<{propertyType.ToFirstCharacterUpperCase()}>({propertyType}.createFromDiscriminatorValue)",
        };
    }
    protected void WriteRequestExecutorBody(CodeMethod codeElement, RequestParams requestParams, CodeClass parentClass, bool isVoid, string returnTypeWithoutCollectionInformation, LanguageWriter writer)
    {
        ArgumentNullException.ThrowIfNull(codeElement);
        ArgumentNullException.ThrowIfNull(requestParams);
        ArgumentNullException.ThrowIfNull(parentClass);
        ArgumentNullException.ThrowIfNull(writer);
        if (codeElement.HttpMethod == null) throw new InvalidOperationException("http method cannot be null");

        var generatorMethodName = parentClass
                                            .Methods
                                            .FirstOrDefault(x => x.IsOfKind(CodeMethodKind.RequestGenerator) && x.HttpMethod == codeElement.HttpMethod)
                                            ?.Name;
        var parametersList = new CodeParameter?[] { requestParams.requestBody, requestParams.requestContentType, requestParams.requestConfiguration }
                            .Select(static x => x?.Name).Where(static x => x != null).Aggregate(static (x, y) => $"{x}, {y}");
        writer.WriteLine($"var requestInfo = {generatorMethodName}({parametersList});");
        var errorMappingVarName = "{}";
        if (codeElement.ErrorMappings.Any())
        {
            errorMappingVarName = "errorMapping";
            writer.WriteLine($"var {errorMappingVarName} = {{");
            writer.IncreaseIndent();
            foreach (var errorMapping in codeElement.ErrorMappings.Where(errorMapping => errorMapping.Value.AllTypes.FirstOrDefault()?.TypeDefinition is CodeClass))
            {
                writer.WriteLine($"\"{errorMapping.Key.ToUpperInvariant()}\" :  {conventions.GetTypeString(errorMapping.Value, codeElement, false)}.createFromDiscriminatorValue,");
            }
            writer.CloseBlock("};");
        }
        var returnTypeCodeType = codeElement.ReturnType as CodeType;
        var returnTypeFactory = returnTypeCodeType?.TypeDefinition is CodeClass
                                ? $", {returnTypeWithoutCollectionInformation}.createFromDiscriminatorValue"
                                : null;
        var prefix = (isVoid, codeElement.ReturnType.IsCollection) switch
        {
            (true, _) => string.Empty,
            (_, true) => "var collectionResult = ",
            (_, _) => "return ",
        };
        writer.WriteLine($"{prefix}await requestAdapter.{GetSendRequestMethodName(isVoid, codeElement, codeElement.ReturnType)}(requestInfo{returnTypeFactory}, {errorMappingVarName});");
        if (codeElement.ReturnType.IsCollection)
            writer.WriteLine("return collectionResult?.ToList();");
    }
    private const string RequestInfoVarName = "requestInfo";
    private void WriteRequestGeneratorBody(CodeMethod codeElement, RequestParams requestParams, CodeClass currentClass, LanguageWriter writer)
    {
        if (codeElement.HttpMethod == null) throw new InvalidOperationException("http method cannot be null");
        if (currentClass.GetPropertyOfKind(CodePropertyKind.PathParameters) is not CodeProperty urlTemplateParamsProperty) throw new InvalidOperationException("path parameters property cannot be null");
        if (currentClass.GetPropertyOfKind(CodePropertyKind.UrlTemplate) is not CodeProperty urlTemplateProperty) throw new InvalidOperationException("url template property cannot be null");

        var operationName = codeElement.HttpMethod.ToString();
        writer.WriteLine($"var {RequestInfoVarName} = RequestInformation(httpMethod : HttpMethod.{operationName?.ToLowerInvariant()}, {currentClass.GetPropertyOfKind(CodePropertyKind.UrlTemplate)?.Name} : {GetPropertyCall(urlTemplateProperty, "string.Empty")}, {currentClass.GetPropertyOfKind(CodePropertyKind.PathParameters)?.Name} :  {GetPropertyCall(urlTemplateParamsProperty, "string.Empty")});");

        if (requestParams.requestConfiguration != null)
            writer.WriteLine($"{RequestInfoVarName}.configure({requestParams.requestConfiguration.Name});");

        if (codeElement.ShouldAddAcceptHeader)
            writer.WriteLine($"{RequestInfoVarName}.headers.put(\"Accept\", \"{codeElement.AcceptHeaderValue}\");");
        if (requestParams.requestBody != null)
        {
            var suffix = requestParams.requestBody.Type.IsCollection ? "Collection" : string.Empty;
            if (requestParams.requestBody.Type.Name.Equals(conventions.StreamTypeName, StringComparison.OrdinalIgnoreCase))
            {
                if (requestParams.requestContentType is not null)
                    writer.WriteLine($"{RequestInfoVarName}.setStreamContent({requestParams.requestBody.Name}, {requestParams.requestContentType.Name});");
                else if (!string.IsNullOrEmpty(codeElement.RequestBodyContentType))
                    writer.WriteLine($"{RequestInfoVarName}.setStreamContent({requestParams.requestBody.Name}, \"{codeElement.RequestBodyContentType}\");");
            }
            else if (currentClass.GetPropertyOfKind(CodePropertyKind.RequestAdapter) is CodeProperty requestAdapterProperty)
                if (requestParams.requestBody.Type is CodeType bodyType && (bodyType.TypeDefinition is CodeClass || bodyType.Name.Equals("MultipartBody", StringComparison.OrdinalIgnoreCase)))
                    writer.WriteLine($"{RequestInfoVarName}.setContentFromParsable({requestAdapterProperty.Name.ToFirstCharacterLowerCase()}, \"{codeElement.RequestBodyContentType}\", {requestParams.requestBody.Name});");
                else
                    writer.WriteLine($"{RequestInfoVarName}.setContentFromScalar{suffix}({requestAdapterProperty.Name.ToFirstCharacterLowerCase()}, \"{codeElement.RequestBodyContentType}\", {requestParams.requestBody.Name});");
        }

        writer.WriteLine($"return {RequestInfoVarName};");
    }
    private static string GetPropertyCall(CodeProperty property, string defaultValue) => property == null ? defaultValue : $"{property.Name.ToFirstCharacterLowerCase()}";
    private void WriteSerializerBody(bool shouldHide, CodeMethod method, CodeClass parentClass, LanguageWriter writer)
    {
        if (parentClass.DiscriminatorInformation.ShouldWriteDiscriminatorForUnionType)
            WriteSerializerBodyForUnionModel(method, parentClass, writer);
        else if (parentClass.DiscriminatorInformation.ShouldWriteDiscriminatorForIntersectionType)
            WriteSerializerBodyForIntersectionModel(method, parentClass, writer);
        else
            WriteSerializerBodyForInheritedModel(shouldHide, method, parentClass, writer);

        if (parentClass.GetPropertyOfKind(CodePropertyKind.AdditionalData) is CodeProperty additionalDataProperty)
            writer.WriteLine($"writer.writeAdditionalData({additionalDataProperty.Name.ToFirstCharacterLowerCase()});");
    }
    private void WriteSerializerBodyForInheritedModel(bool shouldHide, CodeMethod method, CodeClass parentClass, LanguageWriter writer)
    {
        if (shouldHide)
            writer.WriteLine("super.serialize(writer);");
        foreach (var otherProp in parentClass
                                        .GetPropertiesOfKind(CodePropertyKind.Custom)
                                        .Where(static x => !x.ExistsInBaseType && !x.ReadOnly)
                                        .OrderBy(static x => x.Name))
        {
            var serializationMethodName = GetSerializationMethodName(otherProp.Type, method);
            writer.WriteLine($"writer.{serializationMethodName}(\"{otherProp.WireName}\", {otherProp.Name.ToFirstCharacterLowerCase()});");
        }
    }
    private void WriteSerializerBodyForUnionModel(CodeMethod method, CodeClass parentClass, LanguageWriter writer)
    {
        var includeElse = false;
        foreach (var otherProp in parentClass
                                        .GetPropertiesOfKind(CodePropertyKind.Custom)
                                        .Where(static x => !x.ExistsInBaseType)
                                        .OrderBy(static x => x, CodePropertyTypeForwardComparer)
                                        .ThenBy(static x => x.Name))
        {
            writer.WriteLine($"{(includeElse ? "else " : string.Empty)}if({otherProp.Name.ToFirstCharacterUpperCase()} != null) {{");
            writer.IncreaseIndent();
            writer.WriteLine($"writer.{GetSerializationMethodName(otherProp.Type, method)}(null, {otherProp.Name.ToFirstCharacterUpperCase()});");
            writer.CloseBlock();
            if (!includeElse)
                includeElse = true;
        }
    }
    private void WriteSerializerBodyForIntersectionModel(CodeMethod method, CodeClass parentClass, LanguageWriter writer)
    {
        var includeElse = false;
        foreach (var otherProp in parentClass
                                        .GetPropertiesOfKind(CodePropertyKind.Custom)
                                        .Where(static x => !x.ExistsInBaseType)
                                        .Where(static x => x.Type is not CodeType propertyType || propertyType.IsCollection || propertyType.TypeDefinition is not CodeClass)
                                        .OrderBy(static x => x, CodePropertyTypeBackwardComparer)
                                        .ThenBy(static x => x.Name))
        {
            writer.WriteLine($"{(includeElse ? "else " : string.Empty)}if({otherProp.Name.ToFirstCharacterUpperCase()} != null) {{");
            writer.IncreaseIndent();
            writer.WriteLine($"writer.{GetSerializationMethodName(otherProp.Type, method)}(null, {otherProp.Name.ToFirstCharacterUpperCase()});");
            writer.CloseBlock();
            if (!includeElse)
                includeElse = true;
        }
        var complexProperties = parentClass.GetPropertiesOfKind(CodePropertyKind.Custom)
                                            .Where(static x => x.Type is CodeType propType && propType.TypeDefinition is CodeClass && !x.Type.IsCollection)
                                            .ToArray();
        if (complexProperties.Length != 0)
        {
            if (includeElse)
            {
                writer.WriteLine("else {");
                writer.IncreaseIndent();
            }
            var propertiesNames = complexProperties
                                .Select(static x => x.Name.ToFirstCharacterUpperCase())
                                .OrderBy(static x => x)
                                .Aggregate(static (x, y) => $"{x}, {y}");
            writer.WriteLine($"writer.{GetSerializationMethodName(complexProperties.First().Type, method)}(null, {propertiesNames});");
            if (includeElse)
            {
                writer.CloseBlock();
            }
        }
    }

    protected virtual void WriteCommandBuilderBody(CodeMethod codeElement, CodeClass parentClass, RequestParams requestParams, bool isVoid, string returnType, LanguageWriter writer)
    {
        throw new InvalidOperationException("CommandBuilder methods are not implemented in this SDK. They're currently only supported in the shell language.");
    }

    protected string GetSendRequestMethodName(bool isVoid, CodeElement currentElement, CodeTypeBase returnType)
    {
        ArgumentNullException.ThrowIfNull(returnType);
        var returnTypeName = conventions.GetTypeString(returnType, currentElement, false);
        var isStream = conventions.StreamTypeName.Equals(returnTypeName, StringComparison.OrdinalIgnoreCase);
        var isEnum = returnType is CodeType codeType && codeType.TypeDefinition is CodeEnum;
        if (isVoid) return "sendNoContent";
        else if (isStream || conventions.IsPrimitiveType(returnTypeName) || isEnum)
            if (returnType.IsCollection)
                return $"sendPrimitiveCollectionAsync<{returnTypeName}>";
            else
                return $"sendPrimitive<{returnTypeName}>";
        else if (returnType.IsCollection) return $"sendCollection<{returnTypeName}>";
        else return $"send<{returnTypeName}>";
    }
    private void WriteMethodDocumentation(CodeMethod code, LanguageWriter writer)
    {
        conventions.WriteLongDescription(code, writer);
        foreach (var paramWithDescription in code.Parameters
                                                .Where(static x => x.Documentation.DescriptionAvailable)
                                                .OrderBy(static x => x.Name, StringComparer.OrdinalIgnoreCase))
            writer.WriteLine($"{conventions.DocCommentPrefix}<param name=\"{paramWithDescription.Name.ToFirstCharacterLowerCase()}\">{paramWithDescription.Name}</param>");
        conventions.WriteDeprecationAttribute(code, writer);
    }
    private static readonly BaseCodeParameterOrderComparer parameterOrderComparer = new();
    private static string GetBaseSuffix(bool isConstructor, bool inherits, CodeClass parentClass, CodeMethod currentMethod)
    {
        if (isConstructor && inherits)
        {
            if (parentClass.IsOfKind(CodeClassKind.RequestBuilder) && parentClass.Properties.FirstOrDefaultOfKind(CodePropertyKind.UrlTemplate) is CodeProperty urlTemplateProperty &&
                !string.IsNullOrEmpty(urlTemplateProperty.DefaultValue))
            {
                var thirdParameterName = string.Empty;
                if (currentMethod.Parameters.OfKind(CodeParameterKind.PathParameters) is CodeParameter pathParametersParameter)
                    thirdParameterName = $", {pathParametersParameter.Name}";
                else if (currentMethod.Parameters.OfKind(CodeParameterKind.RawUrl) is CodeParameter rawUrlParameter)
                    thirdParameterName = $", {{RequestInformation.rawUrlKey : {rawUrlParameter.Name}}}";
                else if (parentClass.Properties.FirstOrDefaultOfKind(CodePropertyKind.PathParameters) is CodeProperty pathParametersProperty && !string.IsNullOrEmpty(pathParametersProperty.DefaultValue))
                    thirdParameterName = $", {pathParametersProperty.DefaultValue}";
                if (currentMethod.Parameters.OfKind(CodeParameterKind.RequestAdapter) is CodeParameter requestAdapterParameter)
                {
                    return $" : super({requestAdapterParameter.Name.ToFirstCharacterLowerCase()}, {urlTemplateProperty.DefaultValue}{thirdParameterName})";
                }
                else if (parentClass.StartBlock?.Inherits?.Name?.Contains("CliRequestBuilder", StringComparison.Ordinal) == true)
                {
                    // CLI uses a different base class.
                    return $" : super({urlTemplateProperty.DefaultValue}{thirdParameterName})";
                }
            }
            return " : super()";
        }
        else if (isConstructor && parentClass.Properties.Where(x => x.IsOfKind(CodePropertyKind.AdditionalData)).Any())
        {
            return " : ";
        }

        return string.Empty;
    }
    private void WriteMethodPrototype(CodeMethod code, CodeClass parentClass, LanguageWriter writer, string returnType, bool inherits, bool isVoid)
    {
        var staticModifier = code.IsStatic ? "static " : string.Empty;
        if (code.Kind == CodeMethodKind.Serializer || code.Kind == CodeMethodKind.Deserializer)
        {
            writer.WriteLine("@override");
        }
        var genericTypePrefix = isVoid ? string.Empty : "<";
        var genericTypeSuffix = code.IsAsync && !isVoid ? ">" : string.Empty;
        var isConstructor = code.IsOfKind(CodeMethodKind.Constructor, CodeMethodKind.ClientConstructor, CodeMethodKind.RawUrlConstructor);
        var voidCorrectedTaskReturnType = code.IsAsync && isVoid ? "void" : returnType;
        var async = code.IsAsync ? " async" : string.Empty;
        if (code.ReturnType.IsArray && code.IsOfKind(CodeMethodKind.RequestExecutor))
            voidCorrectedTaskReturnType = $"IEnumerable<{voidCorrectedTaskReturnType.StripArraySuffix()}>";
        else if (code.IsOfKind(CodeMethodKind.RequestExecutor) && code.IsAsync)
            voidCorrectedTaskReturnType = $"Future<{voidCorrectedTaskReturnType.StripArraySuffix()}>";
        // TODO: Task type should be moved into the refiner
        var completeReturnType = isConstructor ?
            string.Empty : voidCorrectedTaskReturnType + " ";
        var baseSuffix = GetBaseSuffix(isConstructor, inherits, parentClass, code);
        var parameters = string.Join(", ", code.Parameters.OrderBy(x => x, parameterOrderComparer).Select(p => conventions.GetParameterSignature(p, code)).ToList());
        var methodName = GetMethodName(code, parentClass, isConstructor);
        var includeNullableReferenceType = code.IsOfKind(CodeMethodKind.RequestExecutor, CodeMethodKind.RequestGenerator);
        var openingBracket = baseSuffix.Equals(" : ", StringComparison.Ordinal) ? "" : "{";
        if (includeNullableReferenceType)
        {
            var completeReturnTypeWithNullable = isConstructor || string.IsNullOrEmpty(genericTypeSuffix) ? completeReturnType : $"{completeReturnType[..^2].TrimEnd('?')}?{genericTypeSuffix} ";
            var nullableParameters = string.Join(", ", code.Parameters.Order(parameterOrderComparer)
                                                          .Select(p => p.IsOfKind(CodeParameterKind.RequestConfiguration) ?
                                                                                        GetParameterSignatureWithNullableRefType(p, code) :
                                                                                        conventions.GetParameterSignature(p, code))
                                                          .ToList());
            writer.WriteLine($"{conventions.GetAccessModifier(code.Access)} {staticModifier}{completeReturnTypeWithNullable}{methodName}({nullableParameters}){baseSuffix}{async} {{");
        }
        else
        {
            writer.WriteLine($"{conventions.GetAccessModifier(code.Access)} {staticModifier}{completeReturnType}{methodName}({parameters}){baseSuffix}{async} {openingBracket}");
        }
    }

    private static string GetMethodName(CodeMethod code, CodeClass parentClass, bool isConstructor)
    {
        if (code.IsOfKind(CodeMethodKind.RawUrlConstructor))
        {
            return parentClass.Name.ToFirstCharacterUpperCase() + ".withUrl";
        }
        return isConstructor ? parentClass.Name.ToFirstCharacterUpperCase() : code.Name.ToFirstCharacterLowerCase();
    }

    private string GetParameterSignatureWithNullableRefType(CodeParameter parameter, CodeElement targetElement)
    {
        var signatureSegments = conventions.GetParameterSignature(parameter, targetElement).Split(" ", StringSplitOptions.RemoveEmptyEntries);
        var nullablesegment = signatureSegments[0];
        if (!"void".Equals(nullablesegment, StringComparison.Ordinal))
        {
            nullablesegment = nullablesegment + "?";
        }
        return $"{nullablesegment} {string.Join(" ", signatureSegments[1..])}";
    }
    private string GetSerializationMethodName(CodeTypeBase propType, CodeMethod method, bool includeNullableRef = false)
    {
        var isCollection = propType.CollectionKind != CodeTypeBase.CodeTypeCollectionKind.None;
        var propertyType = conventions.GetTypeString(propType, method, false);
        if (propType is CodeType currentType)
        {
            if (isCollection)
                if (currentType.TypeDefinition == null)
                    return $"writeCollectionOfPrimitiveValues<{propertyType}>";
                else if (currentType.TypeDefinition is CodeEnum)
                    return $"writeCollectionOfEnumValues<{propertyType.TrimEnd('?')}>";
                else
                    return $"writeCollectionOfObjectValues<{propertyType}>";
            else if (currentType.TypeDefinition is CodeEnum enumType)
                return $"writeEnumValue<{enumType.Name.ToFirstCharacterUpperCase()}>";

        }
        return propertyType switch
        {
            "byte[]" => "writeByteArrayValue",
            "String" => "writeStringValue",
            _ when conventions.IsPrimitiveType(propertyType) => $"write{propertyType.TrimEnd(DartConventionService.NullableMarker).ToFirstCharacterUpperCase()}Value",
            _ => $"writeObjectValue<{propertyType.ToFirstCharacterUpperCase()}{(includeNullableRef ? "?" : "")}>",
        };
    }
}