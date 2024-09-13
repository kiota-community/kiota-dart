﻿using System;
using Kiota.Builder.CodeDOM;
using Kiota.Builder.Extensions;

namespace Kiota.Builder.Writers.Dart;
public class CodeIndexerWriter : BaseElementWriter<CodeIndexer, DartConventionService>
{
    public CodeIndexerWriter(DartConventionService conventionService) : base(conventionService) { }
    public override void WriteCodeElement(CodeIndexer codeElement, LanguageWriter writer)
    {
        ArgumentNullException.ThrowIfNull(codeElement);
        ArgumentNullException.ThrowIfNull(writer);
        if (codeElement.Parent is not CodeClass parentClass) throw new InvalidOperationException("The parent of a property should be a class");
        var returnType = conventions.GetTypeString(codeElement.ReturnType, codeElement);
        conventions.WriteShortDescription(codeElement, writer);
        // TODO Kees, make it work:
        // writer.WriteLine($"{conventions.DocCommentPrefix}<param name=\"position\">{codeElement.IndexParameter.Documentation.Description.CleanupXMLString()}</param>");
        conventions.WriteDeprecationAttribute(codeElement, writer);
        writer.StartBlock($"public {returnType} this[{conventions.GetTypeString(codeElement.IndexParameter.Type, codeElement)} position] {{ get {{");
        if (parentClass.GetPropertyOfKind(CodePropertyKind.PathParameters) is CodeProperty pathParametersProp)
            conventions.AddParametersAssignment(writer, pathParametersProp.Type, pathParametersProp.Name.ToFirstCharacterUpperCase(), string.Empty, (codeElement.IndexParameter.Type, codeElement.IndexParameter.SerializationName, "position"));
        conventions.AddRequestBuilderBody(parentClass, returnType, writer, conventions.TempDictionaryVarName, "return ");
        writer.CloseBlock("} }");
    }
}