using CSharpQuest.Server.Execution;

namespace CSharpQuest.Server.Feedback;

/// <summary>Zet exceptions en andere uitvoeringsproblemen om in begrijpelijke uitleg.</summary>
public static class RuntimeErrorTranslator
{
    sealed record Entry(string Title, string Explanation, string Tip);

    static readonly Dictionary<string, Entry> Table = new()
    {
        ["System.FormatException"] = new("FormatException: de tekst kon niet omgezet worden",
            "Parse (bv. int.Parse of double.Parse) kreeg tekst die geen geldig getal is, bv. \"abc\", een lege regel, of \"3.5\" terwijl de Belgische notatie een komma verwacht (3,5).",
            "Gebruik TryParse om ongeldige invoer op te vangen zonder dat je programma crasht. Kommagetallen geef je in met een komma (bv. 3,5)."),
        ["System.IndexOutOfRangeException"] = new("IndexOutOfRangeException: index buiten de grenzen",
            "Je gebruikte een index die niet bestaat. Bij een array of string van lengte n zijn de geldige indexen 0 t.e.m. n-1.",
            "Controleer de voorwaarde van je loop: gebruik i < lengte (niet i <= lengte) en start bij 0."),
        ["System.ArgumentOutOfRangeException"] = new("ArgumentOutOfRangeException: waarde buiten het toegelaten bereik",
            "Een method kreeg een argument dat buiten het toegelaten bereik valt, bv. een te grote index bij Substring, Remove, Insert of bij een List.",
            "Controleer de startpositie en lengte die je meegeeft. Bij Substring(start, lengte) moet start + lengte <= tekst.Length zijn."),
        ["System.NullReferenceException"] = new("NullReferenceException: je gebruikt een variabele die naar niets (null) verwijst",
            "Een reference type variabele bevat null: er is (nog) geen object aan gekoppeld. Wanneer je via die variabele een member gebruikt (met de punt), crasht het programma.",
            "Zoek welke variabele null is op de vermelde regel. Werd het object wel aangemaakt met new? Werd elk element van de array ingevuld?"),
        ["System.DivideByZeroException"] = new("DivideByZeroException: deling door nul",
            "Bij een gehele deling (int) door 0 bestaat er geen resultaat. (Bij double geeft dit 'oneindig', bij int een crash.)",
            "Controleer met een if of de deler verschillend is van 0 voordat je deelt."),
        ["System.OverflowException"] = new("OverflowException: het getal past niet in het datatype",
            "De waarde is te groot of te klein voor het datatype (bv. meer dan 2147483647 voor een int).",
            "Gebruik een groter datatype (long, decimal) of controleer de invoer."),
        ["System.InvalidCastException"] = new("InvalidCastException: ongeldige omzetting",
            "Een waarde kon niet naar het gevraagde type omgezet worden.",
            "Controleer het werkelijke type van de waarde."),
        ["System.ArgumentNullException"] = new("ArgumentNullException: een argument is null",
            "Een method kreeg null terwijl ze een echte waarde verwacht. Vaak gebeurt dit wanneer er geen invoer meer is (Console.ReadLine() geeft dan null).",
            "Controleer dat de variabele die je meegeeft niet null is."),
        ["System.ArgumentException"] = new("ArgumentException: ongeldig argument",
            "Een method kreeg een argument dat niet toegelaten is. Bij een Dictionary gebeurt dit bijvoorbeeld wanneer je met Add een key toevoegt die al bestaat.",
            "Lees de boodschap van de exception. Bij een Dictionary: controleer eerst met ContainsKey of gebruik dict[key] = waarde."),
        ["System.Collections.Generic.KeyNotFoundException"] = new("KeyNotFoundException: de key bestaat niet in de dictionary",
            "Je vroeg met dict[key] een waarde op voor een key die niet in de dictionary zit.",
            "Controleer eerst met ContainsKey(key) of gebruik TryGetValue."),
        ["System.InvalidOperationException"] = new("InvalidOperationException: deze bewerking is nu niet geldig",
            "De bewerking past niet bij de huidige toestand van het object. Klassiek voorbeeld: een collectie wijzigen (Add/Remove) terwijl je ze met foreach overloopt.",
            "Verwijder elementen tijdens het overlopen met een for-loop die achteraan begint, of verzamel eerst wat weg moet en verwijder daarna."),
        ["System.IO.FileNotFoundException"] = new("FileNotFoundException: het bestand bestaat niet",
            "Het programma probeerde een bestand te openen dat niet gevonden werd.",
            "Controleer de bestandsnaam en het pad. Vang dit op met try/catch of controleer eerst met File.Exists(pad)."),
        ["System.IO.DirectoryNotFoundException"] = new("DirectoryNotFoundException: de map bestaat niet",
            "Een deel van het pad verwijst naar een map die niet bestaat.",
            "Maak de map eerst aan met Directory.CreateDirectory of controleer het pad (gebruik Path.Combine)."),
        ["System.IO.IOException"] = new("IOException: fout bij het lezen of schrijven van een bestand",
            "Er liep iets mis met het bestand, bv. omdat het nog open staat in een andere stream.",
            "Sluit streams altijd af, bij voorkeur met een using-statement."),
        ["System.UnauthorizedAccessException"] = new("UnauthorizedAccessException: geen toegang",
            "Het programma heeft geen toelating om dit bestand of deze map te gebruiken. In C# Quest kan je enkel bestanden gebruiken in de map van je programma (de sandbox).",
            "Gebruik een relatief pad zoals \"gegevens.txt\" of Path.Combine(Environment.CurrentDirectory, \"gegevens.txt\")."),
        ["System.StackOverflowException"] = new("StackOverflowException: oneindige recursie",
            "Een method roept zichzelf (direct of indirect) eindeloos op. Elke oproep krijgt een nieuw activatierecord op de call stack, tot die vol zit.",
            "Controleer of je recursie een stopvoorwaarde heeft. Let ook op een property die zichzelf aanroept, bv. get { return Naam; } in plaats van get { return _naam; }."),
        ["System.OutOfMemoryException"] = new("OutOfMemoryException: onvoldoende geheugen",
            "Het programma gebruikt meer geheugen dan toegelaten in de sandbox. Vaak voeg je in een oneindige loop elementen toe aan een collectie of string.",
            "Controleer of je loops stoppen."),
        ["System.NotImplementedException"] = new("NotImplementedException: deze method is nog niet uitgewerkt",
            "De method bevat 'throw new NotImplementedException()', een plaatshouder die Visual Studio genereert.",
            "Schrijf de code van de method."),
        ["System.Exception"] = new("Exception", "Je programma gooide een exception.", "Lees de boodschap van de exception en bekijk de vermelde regel."),
    };

    public static void Enrich(RuntimeErrorInfo info)
    {
        if (Table.TryGetValue(info.Type, out var e))
        {
            info.Title = e.Title;
            info.Explanation = e.Explanation;
            info.Tip = e.Tip;
        }
        else
        {
            string shortName = info.Type.Contains('.') ? info.Type[(info.Type.LastIndexOf('.') + 1)..] : info.Type;
            info.Title = shortName + ": je programma crashte";
            info.Explanation = "Tijdens de uitvoering trad een exception op die niet werd opgevangen.";
            info.Tip = "Lees de boodschap en bekijk de vermelde regel. Je kan exceptions opvangen met try/catch.";
        }
    }

    public static (string Title, string Explanation, string Tip) ForStatus(string status) => status switch
    {
        "timeout" => ("Tijdslimiet overschreden",
            "Je programma bleef te lang draaien. Meestal is dit een oneindige loop: de voorwaarde wordt nooit false.",
            "Controleer of de variabele in je loop-voorwaarde in de loop body verandert (bv. i++), en of de voorwaarde ooit false kan worden."),
        "cpuLimit" => ("Rekentijd overschreden",
            "Je programma gebruikte te veel processortijd. Waarschijnlijk een oneindige loop.",
            "Controleer de voorwaarden van je loops."),
        "outputLimit" => ("Te veel output",
            "Je programma schreef extreem veel tekst naar de console. Vaak is dat een oneindige loop met Console.WriteLine erin.",
            "Controleer de stopvoorwaarde van je loop."),
        "stackOverflow" => ("StackOverflow: oneindige recursie",
            "Een method roept zichzelf eindeloos op, waardoor de call stack volloopt.",
            "Controleer de stopvoorwaarde van je recursie of een property die zichzelf aanroept (get { return Naam; })."),
        "inputExhausted" => ("Je programma vraagt meer invoer dan verwacht",
            "Het programma wachtte op invoer (Console.ReadLine of ReadKey), maar in dit testscenario is er geen invoer meer. Je programma vraagt dus vaker om invoer dan de opdracht beschrijft, of je loop stopt niet op het juiste moment.",
            "Vergelijk je programmaverloop met het voorbeeld in de opdracht. Stopt je loop bij de juiste invoer?"),
        "memory" => ("Geheugenlimiet overschreden",
            "Je programma gebruikte te veel geheugen.",
            "Controleer of je in een oneindige loop gegevens toevoegt."),
        _ => ("Uitvoering beëindigd", "", ""),
    };
}
