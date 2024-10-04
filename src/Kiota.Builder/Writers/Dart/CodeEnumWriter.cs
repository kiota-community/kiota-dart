﻿using System;
using System.Collections.Generic;
using System.Linq;

using Kiota.Builder.CodeDOM;
using Kiota.Builder.Extensions;
using Kiota.Builder.Refiners;
using Microsoft.OpenApi.Any;

namespace Kiota.Builder.Writers.Dart;
public class CodeEnumWriter : BaseElementWriter<CodeEnum, DartConventionService>
{
    public static string AutoGenerationHeader => "// <auto-generated/>";
    public CodeEnumWriter(DartConventionService conventionService) : base(conventionService) { }
    public override void WriteCodeElement(CodeEnum codeElement, LanguageWriter writer)
    {
        ArgumentNullException.ThrowIfNull(codeElement);
        ArgumentNullException.ThrowIfNull(writer);
        if (!codeElement.Options.Any())
            return;
        DartReservedNamesProvider reservedNamesProvider = new DartReservedNamesProvider();
        var enumName = codeElement.Name.ToFirstCharacterUpperCase();
        conventions.WriteShortDescription(codeElement, writer);
        conventions.WriteDeprecationAttribute(codeElement, writer);
        writer.StartBlock($"enum {enumName} {{");
        var lastOption = codeElement.Options.Last();

        HashSet<String> usedNames = new HashSet<string>();
        foreach (var option in codeElement.Options)
        {
            conventions.WriteShortDescription(option, writer);
            var correctedName = getCorrectedName(option.Name);
            if (reservedNamesProvider.ReservedNames.Contains(correctedName) || IllegalEnumValue(correctedName))
            {
                correctedName += "Escaped";
            }
            if (!usedNames.Add(correctedName))
            {
                correctedName = option.Name;
                usedNames.Add(correctedName);
            }
            writer.WriteLine($"{correctedName}(\"{option.Name}\"){(option == lastOption ? ";" : ",")}");
        }
        writer.WriteLine($"const {enumName}(this.value);");
        writer.WriteLine("final String value;");
    }

    private bool IllegalEnumValue(string correctedName)
    {
        return correctedName.EqualsIgnoreCase("string") || correctedName.EqualsIgnoreCase("index");
    }

    private string getCorrectedName(string name)
    {
        if (name.Contains('_', StringComparison.Ordinal))
            return name.ToLowerInvariant().ToCamelCase('_');
        return name.All(c => char.IsUpper(c) || char.IsAsciiDigit(c)) ? name.ToLowerInvariant() : name;
    }
}