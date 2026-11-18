using System.Globalization;
using System.Text.RegularExpressions;
using CSharpQuest.Server.Execution;
using Microsoft.CodeAnalysis;

namespace CSharpQuest.Server.Feedback;

/// <summary>
/// Vertaalt Roslyn-diagnostics naar begrijpelijke Nederlandse feedback met uitleg en tip.
/// {0}, {1}, ... verwijzen naar de namen tussen aanhalingstekens in de originele boodschap.
/// </summary>
public static class DiagnosticTranslator
{
    sealed record Entry(string Message, string Explanation, string Tip);

    static readonly Dictionary<string, Entry> Table = new()
    {
        // ------------------------------------------------------------ syntax
        ["CS1002"] = new("Er ontbreekt een puntkomma ;", "In C# eindigt elke opdracht (statement) met een puntkomma. Zo weet de compiler waar de ene opdracht stopt en de volgende begint.", "Zet een ; aan het einde van de regel. Kijk ook naar de regel ervóór: daar ontbreekt hij meestal."),
        ["CS1003"] = new("Syntaxfout: '{0}' verwacht", "De compiler verwachtte op deze plaats een ander teken. Vaak ontbreekt er een haakje, komma of puntkomma.", "Controleer de haakjes en komma's op deze regel en op de regel ervoor."),
        ["CS1026"] = new("Er ontbreekt een sluitend haakje )", "Elk openend haakje ( moet een bijhorend sluitend haakje ) hebben.", "Tel de haakjes op deze regel: evenveel ( als )."),
        ["CS1513"] = new("Er ontbreekt een sluitende accolade }", "Elk code block dat met { begint, moet met } eindigen. Waarschijnlijk ben je er ergens één vergeten (bij een if, loop, method of klasse).", "Zet je cursor naast een accolade: de editor toont dan de bijhorende accolade. Zoek het block dat niet afgesloten wordt."),
        ["CS1514"] = new("Er ontbreekt een openende accolade {", "Na een klasse, method, if of loop volgt een code block dat met { begint.", "Voeg de ontbrekende { toe."),
        ["CS1525"] = new("Onverwacht teken: '{0}'", "Op deze plaats verwachtte de compiler een waarde of expressie (bv. een getal, variabele of berekening), maar vond iets anders.", "Kijk of je niet per ongeluk een operator dubbel typte of een waarde vergat."),
        ["CS1001"] = new("Er wordt een naam (identifier) verwacht", "Hier moet een naam staan, bijvoorbeeld de naam van een variabele of method.", "Controleer of je een naam vergat, of een gereserveerd woord (zoals int of class) als naam gebruikt."),
        ["CS1010"] = new("Een string loopt door tot het einde van de regel", "Een string literal moet op dezelfde regel beginnen en eindigen met \".", "Voeg het sluitende aanhalingsteken \" toe."),
        ["CS1012"] = new("Te veel tekens in een char literal", "Een char bevat precies één teken en staat tussen enkele aanhalingstekens, bv. 'a'. Voor tekst gebruik je dubbele aanhalingstekens: \"abc\".", "Gebruik \"...\" voor een string of zet maar één teken tussen '...'."),
        ["CS1011"] = new("Lege char literal", "Een char moet precies één teken bevatten: '' is niet geldig.", "Gebruik bv. ' ' (spatie) of gebruik een string \"\"."),
        ["CS1022"] = new("Code buiten een klasse of namespace", "Methods en opdrachten moeten binnen een klasse staan. Misschien staat er een accolade } te veel.", "Controleer de accolades: telt elke { een bijhorende }?"),
        ["CS1519"] = new("Ongeldig teken '{0}' in een klasse", "Binnen een klasse (maar buiten een method) mogen enkel declaraties staan: datavelden, properties, constructors en methods. Opdrachten zoals Console.WriteLine horen in een method.", "Verplaats de opdracht naar binnen een method (bv. Main), of controleer of je een accolade te vroeg sloot."),
        ["CS1520"] = new("Een method moet een return type hebben", "Elke method vermeldt wat hij teruggeeft (bv. int, string) of void als hij niets teruggeeft.", "Zet het return type (of void) vóór de naam van de method."),
        ["CS1031"] = new("Er wordt een type verwacht", "Op deze plaats moet een datatype staan, zoals int, double, string of de naam van een klasse.", "Vul het datatype in."),
        ["CS1056"] = new("Onverwacht teken '{0}'", "Dit teken heeft geen betekenis in C# op deze plaats.", "Verwijder het teken of zet het tussen aanhalingstekens als het tekst is."),
        ["CS1009"] = new("Ongeldige escape-reeks in een string", "In een string heeft \\ een speciale betekenis (bv. \\n voor een nieuwe regel). Een onbekende combinatie geeft een fout.", "Gebruik \\\\ voor een backslash, of zet @ voor de string (bv. @\"C:\\map\")."),
        ["CS1039"] = new("Een string wordt niet afgesloten", "Er ontbreekt een afsluitend aanhalingsteken.", "Voeg \" toe aan het einde van de tekst."),
        ["CS1529"] = new("Een using-directive moet bovenaan het bestand staan", "Regels als 'using System;' moeten vóór alle andere code staan.", "Verplaats de using naar de top van het bestand."),
        ["CS1585"] = new("Verkeerde volgorde van modifiers", "Woorden als public, static, void staan in een vaste volgorde: eerst toegankelijkheid (public), dan static, dan het type.", "Bijvoorbeeld: public static void Main()"),
        ["CS8641"] = new("'else' zonder bijhorende 'if'", "Een else moet direct volgen op het block van een if. Misschien staat er een ; na de if of ontbreekt een accolade.", "Controleer of er geen ; achter if (...) staat en of de accolades kloppen."),
        ["CS1023"] = new("Een declaratie mag hier niet als enige opdracht staan", "Een variabele declareren als enige opdracht van een if of loop zonder accolades is niet toegelaten.", "Zet accolades { } rond de body."),

        // ------------------------------------------------------- names/scope
        ["CS0103"] = new("De naam '{0}' bestaat niet op deze plaats", "De compiler kent geen variabele, method of klasse met deze naam hier. Mogelijke oorzaken: een tikfout (C# is hoofdlettergevoelig!), de variabele is nog niet gedeclareerd, of de variabele is out of scope (gedeclareerd in een ander code block).", "Controleer de schrijfwijze (hoofdletters!). Is de variabele gedeclareerd vóór deze regel en in een omsluitend code block?"),
        ["CS0246"] = new("Het type of de namespace '{0}' is onbekend", "De compiler vindt geen klasse met deze naam. Misschien ontbreekt een using-directive, of is er een tikfout.", "Controleer de schrijfwijze. Voor List of Dictionary heb je 'using System.Collections.Generic;' nodig, voor StreamReader 'using System.IO;'."),
        ["CS0234"] = new("'{0}' bestaat niet in namespace '{1}'", "In deze namespace bestaat het gevraagde type niet.", "Controleer de schrijfwijze of zoek in de documentatie in welke namespace het type zit."),
        ["CS0128"] = new("Er bestaat al een variabele '{0}' in deze scope", "Je declareert twee keer een variabele met dezelfde naam in hetzelfde code block.", "Gebruik de bestaande variabele (zonder datatype ervoor) of kies een andere naam."),
        ["CS0136"] = new("De naam '{0}' wordt al gebruikt in een omsluitende scope", "Een variabele met deze naam bestaat al in een omsluitend code block. Een binnenste block mag die naam niet opnieuw declareren.", "Kies een andere naam, of gebruik de bestaande variabele zonder opnieuw te declareren."),
        ["CS0165"] = new("De variabele '{0}' heeft mogelijk nog geen waarde", "Je gebruikt een lokale variabele die (in minstens één scenario) nog geen waarde kreeg. C# laat dat niet toe.", "Geef de variabele een beginwaarde bij de declaratie, of zorg dat ze in elk pad (if én else) een waarde krijgt."),
        ["CS0841"] = new("De variabele '{0}' wordt gebruikt voordat ze gedeclareerd is", "In C# moet je een variabele eerst declareren (regel erboven) voordat je ze kan gebruiken.", "Verplaats de declaratie naar boven, vóór het eerste gebruik."),
        ["CS0100"] = new("Parameter '{0}' komt twee keer voor", "Elke parameter van een method moet een unieke naam hebben.", "Hernoem een van de parameters."),
        ["CS0101"] = new("Er bestaat al een type '{0}' in deze namespace", "Twee klassen (of enums/interfaces) hebben dezelfde naam in dezelfde namespace.", "Hernoem een van de types of verwijder de dubbele definitie."),
        ["CS0102"] = new("Klasse '{0}' bevat al een lid '{1}'", "Een klasse kan niet twee datavelden/properties/methods met exact dezelfde naam hebben (behalve method overloading met andere parameters).", "Hernoem het lid of verwijder het dubbele."),
        ["CS0111"] = new("Er bestaat al een method '{1}' met dezelfde parameters", "Twee methods met dezelfde naam moeten verschillende parametertypes hebben (method overloading).", "Hernoem de method of geef ze andere parameters."),
        ["CS0117"] = new("'{0}' bevat geen definitie voor '{1}'", "Dit type heeft geen lid met deze naam. Vaak een tikfout of verkeerd hoofdlettergebruik (bv. Console.Writeline in plaats van WriteLine).", "Controleer de schrijfwijze. Typ een punt na de naam: de editor toont dan welke leden bestaan."),
        ["CS1061"] = new("'{0}' bevat geen definitie voor '{1}'", "Het type van deze waarde heeft geen method of property met deze naam. Misschien een tikfout, een ontbrekende () of het verkeerde type.", "Controleer de schrijfwijze en het datatype van de variabele. Bv. .Length bij strings en arrays, .Count bij een List."),
        ["CS0120"] = new("Er is een object nodig om '{0}' te gebruiken", "Je roept een instance member (zonder static) op vanuit een static method zoals Main. Instance members horen bij een object.", "Maak de method static (als hij niet bij een object hoort), of maak eerst een object met new en roep de method op via dat object."),
        ["CS0176"] = new("'{0}' is static en hoort bij de klasse, niet bij een object", "Een static member roep je op via de klassenaam, niet via een object.", "Gebruik KlasseNaam.{0} in plaats van object.{0}."),
        ["CS0122"] = new("'{0}' is niet toegankelijk door zijn beveiligingsniveau", "Dit lid is private (of niet public) en kan dus niet van buiten de klasse gebruikt worden.", "Maak het lid public, of beter: voorzie een public property of method die het gewenste gedrag aanbiedt."),
        ["CS0118"] = new("'{0}' is een {1} maar wordt gebruikt als een {2}", "Je gebruikt een naam op een manier die niet past bij wat hij is (bv. een namespace als type, of een variabele als method).", "Controleer of je de juiste naam gebruikt en of er geen naamconflict is met je namespace of klasse."),
        ["CS0119"] = new("'{0}' is een {1}, wat hier niet geldig is", "Je gebruikt een type of method op een plaats waar een waarde verwacht wordt.", "Vergeet je de haakjes () bij een method-oproep, of new bij het maken van een object?"),

        // ---------------------------------------------------------- types
        ["CS0029"] = new("Kan type '{0}' niet automatisch omzetten naar '{1}'", "C# is strikt getypeerd: je kan een waarde van het ene type niet zomaar in een variabele van een ander type stoppen. Een veelvoorkomend voorbeeld: Console.ReadLine() geeft een string, geen int.", "Gebruik int.Parse(...), double.Parse(...) of TryParse om tekst om te zetten naar een getal, of .ToString() voor de omgekeerde richting."),
        ["CS0266"] = new("Kan type '{0}' niet impliciet omzetten naar '{1}' (er bestaat wel een expliciete conversie)", "Deze omzetting kan informatie verliezen (bv. van double naar int: de cijfers na de komma verdwijnen). Daarom doet C# ze niet automatisch.", "Gebruik een expliciete cast, bv. (int)waarde, of Convert.ToInt32(waarde), of rond eerst af met Math.Round."),
        ["CS0030"] = new("Kan type '{0}' niet omzetten naar '{1}'", "Tussen deze twee types bestaat geen conversie.", "Gebruik een Parse-method of een andere aanpak om de waarde om te zetten."),
        ["CS0019"] = new("Operator '{0}' werkt niet tussen '{1}' en '{2}'", "Deze bewerking is niet gedefinieerd voor deze datatypes. Je kan bv. geen string en int vergelijken met <, of bool optellen.", "Zet de waarden eerst om naar hetzelfde, passende type (bv. int.Parse voor tekst)."),
        ["CS0023"] = new("Operator '{0}' werkt niet op een waarde van type '{1}'", "Deze operator kan niet gebruikt worden met dit type.", "Controleer het datatype van de waarde."),
        ["CS1503"] = new("Argument {0}: kan '{1}' niet omzetten naar '{2}'", "Je geeft een waarde van het verkeerde type mee aan een method.", "Kijk welk type de parameter verwacht en zet je waarde om, of geef een andere variabele mee."),
        ["CS1501"] = new("Er bestaat geen variant van '{0}' met {1} argumenten", "Je roept een method op met een verkeerd aantal argumenten.", "Controleer hoeveel parameters de method verwacht (hover over de naam of bekijk de definitie)."),
        ["CS7036"] = new("Er ontbreekt een argument voor parameter '{0}' van '{1}'", "De method of constructor verwacht meer argumenten dan je meegaf.", "Geef een waarde mee voor elke verplichte parameter."),
        ["CS1729"] = new("'{0}' heeft geen constructor met {1} argumenten", "Je maakt een object met new, maar er bestaat geen constructor die dit aantal argumenten aanvaardt.", "Controleer welke constructors de klasse heeft, of voeg een passende constructor toe."),
        ["CS0664"] = new("Een kommagetal-literal is standaard een double", "Een getal zoals 3.5 is in C# een double. Voor decimal moet je er M achter zetten (3.5M), voor float een F (3.5F).", "Schrijf bv. 3.5M voor een decimal of 3.5F voor een float."),
        ["CS0220"] = new("Overloop bij het berekenen van een constante", "De uitkomst past niet in het datatype.", "Gebruik een groter type (bv. long) of controleer de berekening."),
        ["CS0031"] = new("De constante waarde '{0}' past niet in '{1}'", "Het getal is te groot of te klein voor dit datatype.", "Gebruik een groter datatype (long, double, decimal)."),
        ["CS0021"] = new("Indexering met [] is niet mogelijk op een waarde van type '{0}'", "Enkel strings, arrays en collecties (zoals List of Dictionary) kan je met [] indexeren.", "Controleer of de variabele wel een array, string of collectie is."),
        ["CS0131"] = new("De linkerkant van een toekenning moet een variabele zijn", "Bij = staat links altijd de variabele die de nieuwe waarde krijgt, rechts de waarde zelf.", "Draai de toekenning om (variabele = waarde) of gebruik == als je wil vergelijken."),
        ["CS0200"] = new("Property '{0}' is readonly", "Deze property heeft geen set (of een private set) en kan dus niet van buitenaf gewijzigd worden.", "Geef de waarde mee via de constructor, of voeg (indien gewenst) een set toe."),
        ["CS0191"] = new("Een readonly dataveld kan enkel in de constructor een waarde krijgen", "readonly velden krijgen hun waarde bij de declaratie of in de constructor, daarna niet meer.", "Verplaats de toekenning naar de constructor, of verwijder readonly als de waarde moet kunnen veranderen."),
        ["CS0198"] = new("Een static readonly dataveld kan niet gewijzigd worden", "Na initialisatie kan dit veld niet meer veranderen.", "Verwijder readonly als het moet kunnen veranderen."),
        ["CS0272"] = new("De set van '{0}' is niet toegankelijk", "De setter van deze property is private: enkel de klasse zelf kan de waarde wijzigen.", "Wijzig de waarde via een method of constructor van de klasse."),
        ["CS0154"] = new("Property '{0}' heeft geen get", "Je probeert een waarde te lezen uit een property die enkel een set heeft.", "Voeg een get toe aan de property."),
        ["CS0236"] = new("Een veldinitialisatie kan geen ander niet-static lid gebruiken", "Bij de declaratie van een dataveld kan je nog niet verwijzen naar andere datavelden van het object.", "Initialiseer het veld in de constructor."),
        ["CS0815"] = new("Kan geen var-declaratie maken met deze waarde", "Bij var moet de compiler het type kunnen afleiden uit de waarde. Met null of een lambda lukt dat niet.", "Vermeld het datatype expliciet."),
        ["CS0818"] = new("Een var-declaratie moet meteen een waarde krijgen", "Bij var leidt de compiler het type af uit de beginwaarde, dus die is verplicht.", "Geef meteen een waarde, of vermeld het datatype expliciet."),
        ["CS0150"] = new("Er wordt een constante waarde verwacht", "Bij een const of een case moet de waarde vastliggen tijdens het compileren.", "Gebruik een literal of een andere const, of gebruik geen const/case."),
        ["CS0133"] = new("De waarde van const '{0}' moet constant zijn", "Een const krijgt een vaste waarde die al gekend is bij het compileren (bv. 3.14), geen berekening met variabelen of methods.", "Gebruik een gewone variabele of een static readonly veld."),
        ["CS0037"] = new("null kan niet in een value type", "Value types zoals int, double en bool kunnen geen null bevatten.", "Gebruik een geldige standaardwaarde (bv. 0), of int? als je echt 'geen waarde' wil voorstellen."),

        // ------------------------------------------------------- statements
        ["CS0201"] = new("Deze expressie kan niet als opdracht gebruikt worden", "Enkel toekenningen, method-oproepen, ++/-- en new kunnen als losse opdracht staan. Een vergelijking of berekening op zichzelf doet niets.", "Bedoelde je een toekenning (=) in plaats van een vergelijking (==)? Of vergat je de waarde in een variabele te stoppen?"),
        ["CS0161"] = new("Niet alle codepaden geven een waarde terug in '{0}'", "Een method met een return type (geen void) moet in élk mogelijk scenario een return bereiken. Bv. als enkel binnen een if een return staat, ontbreekt de return voor het geval de voorwaarde false is.", "Zorg dat er onderaan de method (of in elke tak van if/else) een return staat."),
        ["CS0127"] = new("Een void method kan geen waarde teruggeven", "Een method met return type void geeft niets terug; 'return waarde;' mag daar niet.", "Verander void in het juiste type, of gebruik enkel 'return;' zonder waarde."),
        ["CS0126"] = new("Er wordt een return-waarde verwacht", "Deze method heeft een return type en moet dus 'return waarde;' gebruiken.", "Geef een waarde van het juiste type terug."),
        ["CS0139"] = new("break of continue staat niet in een loop", "break en continue kunnen enkel binnen een for, foreach, while of do while (of break ook in een switch).", "Verwijder de break/continue of zet hem binnen een loop."),
        ["CS0163"] = new("Een case in een switch moet eindigen met break", "In C# mag je niet 'doorvallen' van de ene case naar de volgende.", "Zet een break; (of return) op het einde van elke case."),
        ["CS0152"] = new("Deze case-waarde komt twee keer voor", "Elke case in een switch moet een unieke waarde hebben.", "Verwijder of wijzig de dubbele case."),
        ["CS0428"] = new("Een method-naam wordt als waarde gebruikt", "Je verwijst naar een method zonder haakjes, waardoor ze niet wordt opgeroepen.", "Zet haakjes achter de method-naam, bv. Console.ReadLine() of tekst.ToUpper()."),
        ["CS1620"] = new("Argument moet met '{1}' doorgegeven worden", "Deze parameter is een ref- of out-parameter. Je moet het sleutelwoord ook bij de oproep vermelden.", "Bijvoorbeeld: int.TryParse(tekst, out getal)."),
        ["CS1615"] = new("Argument mag niet met '{1}' doorgegeven worden", "Deze parameter is geen ref/out-parameter.", "Verwijder ref/out bij de oproep."),
        ["CS0177"] = new("De out-parameter '{0}' moet een waarde krijgen", "Een method met een out-parameter moet die parameter in elk scenario een waarde geven voor hij eindigt.", "Ken een waarde toe aan de out-parameter."),
        ["CS0269"] = new("De out-parameter '{0}' heeft nog geen waarde", "Je leest een out-parameter voordat je hem een waarde gaf.", "Ken eerst een waarde toe."),
        ["CS0170"] = new("Het veld '{0}' heeft mogelijk nog geen waarde", "Je gebruikt een veld van een struct dat nog niet ingevuld is.", "Ken het veld eerst een waarde toe."),

        // ------------------------------------------------------- OO / classes
        ["CS0535"] = new("'{0}' implementeert '{1}' van de interface niet", "Een klasse die een interface implementeert, moet álle members uit die interface voorzien (met dezelfde naam, parameters en return type, en public).", "Voeg de ontbrekende member toe, public en met exact dezelfde signatuur. Tip: Ctrl+. in Visual Studio genereert de members."),
        ["CS0738"] = new("'{0}' implementeert '{1}' niet: verkeerd return type", "De method bestaat wel, maar heeft een ander return type dan in de interface.", "Gebruik exact hetzelfde return type als in de interface."),
        ["CS0737"] = new("'{0}' implementeert '{1}' niet: de member is niet public", "Members die een interface implementeren moeten public zijn.", "Zet public voor de member."),
        ["CS0144"] = new("Je kan geen object maken van de abstracte klasse of interface '{0}'", "Een interface beschrijft enkel wat een type kan, het is geen 'echte' klasse. Je maakt objecten van klassen die de interface implementeren.", "Gebruik new met een concrete klasse, bv. IFiguur f = new Cirkel(...);"),
        ["CS0106"] = new("De modifier '{0}' is hier niet geldig", "Dit sleutelwoord mag niet op deze plaats (bv. public bij een lokale variabele, of access modifiers in een interface in oudere C#-versies).", "Verwijder de modifier."),
        ["CS0108"] = new("'{0}' verbergt het geërfde lid '{1}'", "Een lid met dezelfde naam bestaat al in een basisklasse.", "Hernoem het lid."),
        ["CS0050"] = new("Inconsistente toegankelijkheid: het return type '{1}' is minder toegankelijk dan method '{0}'", "Een public method kan geen type teruggeven dat zelf niet public is.", "Maak de klasse public, of de method minder toegankelijk."),
        ["CS0051"] = new("Inconsistente toegankelijkheid: parametertype '{1}' is minder toegankelijk dan '{0}'", "Een public method kan geen parameter hebben van een type dat niet public is.", "Maak de klasse van de parameter public."),
        ["CS0052"] = new("Inconsistente toegankelijkheid: veldtype '{1}' is minder toegankelijk dan veld '{0}'", "Een public veld/property kan geen type hebben dat niet public is.", "Maak de klasse public."),
        ["CS0053"] = new("Inconsistente toegankelijkheid: property-type '{1}' is minder toegankelijk dan property '{0}'", "Een public property kan geen type hebben dat niet public is.", "Maak de klasse public."),
        ["CS0026"] = new("'this' kan niet in een static member", "In een static method is er geen huidig object, dus 'this' bestaat daar niet.", "Maak de method niet-static, of gebruik geen this."),
        ["CS0027"] = new("'this' is hier niet beschikbaar", "this kan enkel binnen methods, properties en constructors van een object.", "Verplaats de code naar een constructor of method."),
        ["CS0542"] = new("Een member mag niet dezelfde naam hebben als de klasse", "Enkel de constructor heeft dezelfde naam als de klasse.", "Hernoem de member."),
        ["CS0702"] = new("Ongeldige constraint", "Deze type-constraint is niet toegelaten.", "Verwijder de constraint."),
        ["CS0311"] = new("Het type '{0}' kan niet gebruikt worden als typeparameter '{2}'", "Het type voldoet niet aan de voorwaarden van deze generieke klasse of method.", "Kies een type dat aan de voorwaarden voldoet."),
        ["CS0305"] = new("Het generieke type '{0}' verwacht {1} typeargument(en)", "Bij een generiek type zoals List<T> of Dictionary<TKey, TValue> moet je de types tussen < > vermelden.", "Bv. List<int> of Dictionary<string, int>."),
        ["CS1579"] = new("foreach werkt niet op '{0}'", "foreach kan enkel een verzameling overlopen (array, string, List, ...).", "Controleer of je een verzameling meegeeft."),

        // ------------------------------------------------------ entry point
        ["CS5001"] = new("Het programma heeft geen static Main method", "Een C#-programma start altijd in een static Main method. Die ontbreekt (of is verkeerd geschreven, bv. main met kleine letter).", "Voeg 'static void Main(string[] args) { ... }' toe in een klasse, of controleer de schrijfwijze."),
        ["CS1555"] = new("Het opstartobject '{0}' bestaat niet", "Er werd een opstartobject (startup object) gekozen, maar er bestaat geen klasse met die volledige naam (namespace + klasse) en een static Main method.", "Controleer de naam van je klasse en namespace, of voeg het bestand met die klasse toe."),
        ["CS0017"] = new("Het programma heeft meer dan één Main method (meerdere opstartobjecten)", "Er zijn meerdere klassen met een Main method. De compiler weet niet waar het programma moet starten.", "Kies een opstartobject in de werkbalk boven de editor (zoals 'Startup object' in de projecteigenschappen van Visual Studio), of verwijder een van de Main methods."),
        ["CS8804"] = new("Een opstartobject kiezen kan niet met top-level statements", "Je project gebruikt top-level statements (code buiten een klasse). Daarbij kan je geen Main-klasse kiezen.", "Zet je code in een klasse met een static void Main method."),
        ["CS7022"] = new("Het programma heeft zowel top-level statements als een Main method", "Code die rechtstreeks buiten een klasse staat (top-level statements), vormt zelf al het startpunt van het programma.", "Zet je code in de Main method, of verwijder de Main method."),

        // ------------------------------------------------------- warnings
        ["CS0168"] = new("De variabele '{0}' wordt gedeclareerd maar nooit gebruikt", "Deze variabele heeft geen nut in je programma.", "Verwijder de variabele, of gebruik ze waar ze nodig is."),
        ["CS0219"] = new("De variabele '{0}' krijgt een waarde maar wordt nooit gebruikt", "Je kent een waarde toe die nergens gelezen wordt.", "Verwijder de variabele of gebruik de waarde."),
        ["CS0162"] = new("Onbereikbare code gedetecteerd", "Deze regels kunnen nooit uitgevoerd worden, bv. omdat er een return of break vóór staat.", "Verwijder de code of verplaats de return/break."),
        ["CS0642"] = new("Mogelijk foute lege opdracht: ; na if/while/for", "Een ; direct na if (...) of while (...) maakt een lege opdracht. Het block eronder wordt dan altijd (of maar één keer) uitgevoerd!", "Verwijder de ; na het haakje."),
        ["CS0665"] = new("Toekenning (=) in een voorwaarde: bedoelde je == ?", "In een voorwaarde vergelijk je met ==. Eén = is een toekenning.", "Gebruik == om te vergelijken."),
        ["CS1717"] = new("Je kent een variabele aan zichzelf toe", "x = x; doet niets. Misschien bedoelde je een dataveld (this.x = x) of een andere variabele.", "Gebruik this.naam = naam of een underscore voor datavelden (_naam = naam)."),
        ["CS0252"] = new("Mogelijk onbedoelde referentievergelijking", "== vergelijkt hier twee objecten op identiteit, niet op inhoud.", "Gebruik .Equals(...) of vergelijk strings als strings."),
        ["CS0253"] = new("Mogelijk onbedoelde referentievergelijking", "== vergelijkt hier twee objecten op identiteit, niet op inhoud.", "Gebruik .Equals(...)."),
        ["CS0414"] = new("Het dataveld '{0}' krijgt een waarde maar wordt nooit gebruikt", "Dit dataveld wordt nooit gelezen.", "Verwijder het veld of gebruik het."),
        ["CS0169"] = new("Het dataveld '{0}' wordt nooit gebruikt", "Dit dataveld heeft geen nut.", "Verwijder het veld of gebruik het."),
        ["CS0649"] = new("Het dataveld '{0}' krijgt nooit een waarde", "Dit veld behoudt altijd zijn defaultwaarde (0, null, false).", "Ken een waarde toe in de constructor of verwijder het veld."),
        ["CS0472"] = new("Deze vergelijking is altijd {0}", "Een value type (zoals int) kan nooit null zijn.", "Verwijder de vergelijking met null."),
        ["CS8321"] = new("De lokale functie '{0}' wordt nooit gebruikt", "Je definieerde een functie binnen een method, maar roept ze nergens op. Misschien staat je method per ongeluk binnen Main?", "Verplaats de method buiten Main (maar binnen de klasse), of roep ze op."),
    };

    static readonly Regex Quoted = new("'([^']*)'", RegexOptions.Compiled);

    public static CodeDiagnostic Translate(Diagnostic d, bool isSyntax)
    {
        string original = d.GetMessage(CultureInfo.InvariantCulture);
        var args = Quoted.Matches(original).Select(m => m.Groups[1].Value).ToArray();
        if (d.Id == "CS1503")
        {
            var argNum = Regex.Match(original, @"Argument (\d+)");
            args = new[] { argNum.Success ? argNum.Groups[1].Value : "?" }.Concat(args).ToArray();
        }
        if (d.Id == "CS1501" || d.Id == "CS1729" || d.Id == "CS0305")
        {
            var num = Regex.Match(original, @"(\d+) (type )?arguments?");
            if (num.Success) args = args.Take(1).Append(num.Groups[1].Value).Concat(args.Skip(1)).ToArray();
        }

        string message, explanation, tip;
        if (Table.TryGetValue(d.Id, out var entry) && entry.Message.Length > 0)
        {
            message = Fill(entry.Message, args);
            explanation = Fill(entry.Explanation, args);
            tip = Fill(entry.Tip, args);
        }
        else
        {
            message = original;
            explanation = isSyntax
                ? "De compiler kan deze code niet lezen: de schrijfwijze klopt niet met de regels (syntax) van C#."
                : "De code is wel leesbaar, maar breekt een regel van de taal C#.";
            tip = $"Zoek op '{d.Id} C#' in de officiële documentatie (learn.microsoft.com) voor meer uitleg.";
        }

        var span = d.Location.GetLineSpan();
        string severity = d.Severity switch
        {
            DiagnosticSeverity.Error => "error",
            DiagnosticSeverity.Warning => "warning",
            _ => "info",
        };
        string category = severity != "error" ? (d.Id is "CS0168" or "CS0219" or "CS0162" or "CS0414" or "CS0169" or "CS8321" ? FeedbackCategory.Redundant : FeedbackCategory.Warning)
            : isSyntax ? FeedbackCategory.Syntax : FeedbackCategory.Compile;

        return new CodeDiagnostic
        {
            Severity = severity,
            Category = category,
            Code = d.Id,
            Message = message,
            Original = original,
            Explanation = explanation,
            Tip = tip,
            File = d.Location.IsInSource ? span.Path : null,
            Line = d.Location.IsInSource ? span.StartLinePosition.Line + 1 : 0,
            Column = d.Location.IsInSource ? span.StartLinePosition.Character + 1 : 0,
            EndLine = d.Location.IsInSource ? span.EndLinePosition.Line + 1 : 0,
            EndColumn = d.Location.IsInSource ? span.EndLinePosition.Character + 1 : 0,
        };
    }

    static string Fill(string template, string[] args)
    {
        return Regex.Replace(template, @"\{(\d)\}", m =>
        {
            int i = m.Groups[1].Value[0] - '0';
            return i < args.Length ? args[i] : "…";
        });
    }

    /// <summary>Lijst van gekende foutcodes, gebruikt in de kennisbank.</summary>
    public static IEnumerable<(string Code, string Message, string Explanation, string Tip)> Catalog() =>
        Table.Where(kv => kv.Value.Message.Length > 0)
             .Select(kv => (kv.Key, kv.Value.Message, kv.Value.Explanation, kv.Value.Tip));
}

