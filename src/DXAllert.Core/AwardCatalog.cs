namespace DXAllert.Core;

/// <summary>
/// Diplomi dell'elenco di VA3RJ (qsl.net/va3rj/awards_dx.html) che si possono calcolare da un log ADIF:
/// paesi DXCC/WAE, continenti, stazioni e prefissi di un paese, isole IOTA. Le regole sono state lette sulle pagine
/// degli enti (novembre 2026). Gli altri diplomi dell'elenco chiedono riferimenti che un log non contiene
/// (castelli, parchi, comuni, province…) oppure valevano solo in un periodo già passato.
/// </summary>
public static class AwardCatalog
{
    public static readonly string[] HfBands = { "160m", "80m", "40m", "30m", "20m", "17m", "15m", "12m", "10m" };
    private static readonly string[] FiveBands = { "80m", "40m", "20m", "15m", "10m" };

    public static readonly IReadOnlyDictionary<string, string> Continents = new Dictionary<string, string>
    {
        ["EU"] = "Europa", ["AS"] = "Asia", ["AF"] = "Africa", ["NA"] = "Nord America", ["SA"] = "Sud America", ["OC"] = "Oceania",
    };

    private static readonly string[] ItalyEntities = { "Italy", "Sardinia" };

    // Paesi elencati dai regolamenti, con i nomi del cty.dat.
    private static readonly string[] SouthernHemisphere =
    {
        "Antarctica", "Botswana", "Tonga", "Nauru", "Mozambique", "Chile", "Easter Island", "San Felix & San Ambrosio",
        "Juan Fernandez Islands", "Bolivia", "Uruguay", "Angola", "Comoros", "North Cook Islands", "South Cook Islands", "Niue",
        "Mayotte", "New Caledonia", "Marquesas Islands", "Austral Islands", "French Polynesia", "Reunion Island", "Glorioso Islands",
        "Juan de Nova, Europa", "Tromelin Island", "Crozet Island", "Kerguelen Islands", "Amsterdam & St. Paul Is.",
        "Wallis & Futuna Islands", "Solomon Islands", "Temotu Province", "Ecuador", "Galapagos Islands", "Swains Island",
        "American Samoa", "Argentina", "Peru", "Papua New Guinea", "Brazil", "Trindade & Martim Vaz", "Fernando de Noronha",
        "Seychelles", "Tuvalu", "Western Kiribati", "Central Kiribati", "Eastern Kiribati", "Banaba Island", "Gabon",
        "Chesterfield Islands", "Namibia", "Australia", "Macquarie Island", "Heard Island", "Cocos (Keeling) Islands",
        "Lord Howe Island", "Mellish Reef", "Norfolk Island", "Willis Island", "Christmas Island", "Pitcairn Island",
        "Ducie Island", "South Shetland Islands", "Falkland Islands", "South Georgia Island", "South Orkney Islands",
        "South Sandwich Islands", "Chagos Islands", "Indonesia", "Vanuatu", "Zimbabwe", "St. Helena", "Ascension Island",
        "Tristan da Cunha & Gough", "New Zealand", "Chatham Islands", "Kermadec Islands", "N.Z. Subantarctic Is.", "Paraguay",
        "South Africa", "Pr. Edward & Marion Is.", "Mauritius", "Rodriguez Island", "Annobon Island", "Conway Reef", "Fiji",
        "Rotuma Island", "Kingdom of Eswatini", "Bouvet", "Peter 1 Island", "Agalega & St. Brandon", "Timor - Leste",
        "Tanzania", "Madagascar", "Samoa", "Lesotho", "Malawi", "Zambia", "Dem. Rep. of the Congo", "Burundi", "Rwanda",
    };

    // Lista WAE del DARC (paesi validi).
    private static readonly string[] WaeCountries =
    {
        "Sov Mil Order of Malta", "Monaco", "Montenegro", "ITU HQ", "Vienna Intl Ctr", "Croatia", "Malta", "Andorra", "Portugal",
        "Azores", "Fed. Rep. of Germany", "Bosnia-Herzegovina", "Spain", "Balearic Islands", "Ireland", "Moldova", "Estonia",
        "Belarus", "France", "England", "Isle of Man", "Northern Ireland", "Jersey", "Scotland", "Shetland Islands", "Guernsey",
        "Wales", "Hungary", "Switzerland", "Liechtenstein", "Vatican City", "Italy", "Sardinia", "Sicily", "Svalbard",
        "Bear Island", "Jan Mayen", "Norway", "Luxembourg", "Lithuania", "Bulgaria", "Austria", "Finland", "Aland Islands",
        "Market Reef", "Czech Republic", "Slovak Republic", "Belgium", "Faroe Islands", "Denmark", "Netherlands",
        "Franz Josef Land", "Slovenia", "Sweden", "Poland", "Greece", "Mount Athos", "Dodecanese", "Crete", "San Marino",
        "European Turkey", "Iceland", "Corsica", "European Russia", "Kaliningrad", "Ukraine", "Latvia", "Romania", "Serbia",
        "Republic of Kosovo", "North Macedonia", "Albania", "Gibraltar",
    };

    public static IReadOnlyList<AwardDef> Build(CtyResolver cty)
    {
        var dxcc = cty.Entities.Select(e => e.Name).ToList();
        var waeAll = cty.Wae.Entities.Select(e => e.Name).ToList();
        var list = new List<AwardDef>();

        // ------------------------------------------------------------ DXCC nel mondo
        list.Add(new AwardDef
        {
            Id = "wia-dxcc", Name = "DXCC (WIA)", Sponsor = "WIA, Australia", Group = "Paesi DXCC",
            Url = "https://www.wia.org.au/members/wiadxawards/dxccopen/",
            Rules = "Entità DXCC attuali, qualunque banda e modo, QSO dal 1° gennaio 1946. Classi da 100, poi ogni 25; " +
                    "Honour Roll a 9 dal totale, Excellence con tutte. Servono le conferme (QSL, LoTW, eQSL AG).",
            Key = q => q.Dxcc?.Name, Items = Plain(dxcc), Map = true, Since = new DateTime(1946, 1, 1),
            Levels = Steps(100, 325, 25).Append(new AwardLevel("Honour Roll", dxcc.Count - 9)).Append(new AwardLevel("Excellence", dxcc.Count)).ToArray(),
        });
        list.Add(new AwardDef
        {
            Id = "wia-5bdxcc", Name = "DXCC 5 bande (WIA)", Sponsor = "WIA, Australia", Group = "Paesi DXCC",
            Url = "https://www.wia.org.au/members/wiadxawards/5bdxcc/",
            Rules = "100 entità DXCC su ognuna di 5 bande HF a scelta (500 in tutto). La tabella mostra le bande migliori.",
            Key = q => q.Dxcc?.Name, Score = AwardScore.BestBands, Bands = HfBands, BandCount = 5, PerBand = 100,
            Since = new DateTime(1946, 1, 1), Levels = new[] { new AwardLevel("5BDXCC", 500) },
        });
        list.Add(new AwardDef
        {
            Id = "wia-9bdxcc", Name = "DXCC 9 bande (WIA)", Sponsor = "WIA, Australia", Group = "Paesi DXCC",
            Url = "https://www.wia.org.au/members/wiadxawards/9bdxcc/",
            Rules = "100 entità DXCC su ognuna di 9 bande HF (900 in tutto).",
            Key = q => q.Dxcc?.Name, Score = AwardScore.BestBands, Bands = HfBands, BandCount = 9, PerBand = 100,
            Since = new DateTime(1946, 1, 1), Levels = new[] { new AwardLevel("9BDXCC", 900) },
        });
        list.Add(new AwardDef
        {
            Id = "wia-elite", Name = "Elite 5 bande DXCC (WIA)", Sponsor = "WIA, Australia", Group = "Paesi DXCC",
            Url = "https://www.wia.org.au/members/wiadxawards/5bdelite/",
            Rules = "200 entità DXCC su ognuna delle bande 80, 40, 20, 15 e 10 m.",
            Key = q => q.Dxcc?.Name, Score = AwardScore.BestBands, Bands = FiveBands, BandCount = 5, PerBand = 200,
            Since = new DateTime(1946, 1, 1), Levels = new[] { new AwardLevel("Elite 5BDXCC", 1000) },
        });
        list.Add(new AwardDef
        {
            Id = "rca-101", Name = "101 Países", Sponsor = "Radio Club Argentino", Group = "Paesi DXCC",
            Url = "https://www.qsl.net/lu5gpl/saltos/awards.htm",
            Rules = "101 entità DXCC, tra cui obbligatoriamente l'Argentina (LU). QSO dal 20 novembre 1945. " +
                    "Certificati CW, fonia o misti; aggiunte a 121, 141, 161…",
            Key = q => q.Dxcc?.Name, Items = Plain(dxcc), Map = true, Required = new[] { "Argentina" },
            Since = new DateTime(1945, 11, 20), Levels = Steps(101, 341, 20, "").ToArray(),
        });
        list.Add(new AwardDef
        {
            Id = "rcv-dx", Name = "Diploma DX 100 / 200 / 300", Sponsor = "Radio Club Venezolano", Group = "Paesi DXCC",
            Url = "https://radioyv.club/diplomadx100.php",
            Rules = "100, 200 o 300 entità DXCC diverse, qualunque banda e modo.",
            Key = q => q.Dxcc?.Name, Items = Plain(dxcc), Map = true,
            Levels = new[] { new AwardLevel("DX 100", 100), new AwardLevel("DX 200", 200), new AwardLevel("DX 300", 300) },
        });
        list.Add(new AwardDef
        {
            Id = "ct-dx100", Name = "CT DX 100", Sponsor = "REP, Portogallo", Group = "Paesi DXCC",
            Url = "https://rep.pt/wp/2017/03/08/diploma-award-ct-dx-100/",
            Rules = "Almeno 100 paesi DXCC, QSO dal 15 novembre 1945, bande 10-80 m. Bollini ogni 25 fino a 300 (Honour Roll).",
            Key = q => q.Dxcc?.Name, Items = Plain(dxcc), Map = true, Since = new DateTime(1945, 11, 15),
            Bands = new[] { "80m", "60m", "40m", "30m", "20m", "17m", "15m", "12m", "10m" },
            Levels = Steps(100, 300, 25).ToArray(),
        });
        list.Add(new AwardDef
        {
            Id = "ure-eadx100", Name = "EADX100", Sponsor = "URE, Spagna", Group = "Paesi DXCC",
            Url = "https://diplomas.ure.es/diplomas/eadx100/bases/",
            Rules = "100 entità attive della lista DXCC e WAE, tra cui obbligatoriamente EA, EA6, EA8 ed EA9. QSO dal 1° aprile 1949.",
            Key = q => q.Wae?.Name, Items = Plain(waeAll), Map = true, Since = new DateTime(1949, 4, 1),
            Required = new[] { "Spain", "Balearic Islands", "Canary Islands", "Ceuta & Melilla" },
            Levels = Steps(100, 200, 25).ToArray(),
        });
        list.Add(new AwardDef
        {
            Id = "ari-toplist", Name = "Toplist Award", Sponsor = "ARI", Group = "Paesi DXCC",
            Url = "https://www.ari.it/en/diplomi-hf/diplomiari/1727-toplist-award.html",
            Rules = "Somma dei paesi DXCC confermati su ogni banda HF da 160 a 10 m (WARC comprese): almeno 1000, poi ogni 100 fino a 2000.",
            Key = q => q.Dxcc?.Name, Score = AwardScore.BandPoints, Bands = HfBands,
            Levels = Steps(1000, 2000, 100).Select(l => l with { Count = 0, Points = l.Count }).ToArray(),
        });
        var south = new HashSet<string>(SouthernHemisphere);
        list.Add(new AwardDef
        {
            Id = "wsh", Name = "Worked Southern Hemisphere (WSH)", Sponsor = "IK3GER", Group = "Paesi DXCC",
            Url = "https://www.ik3ger.it/awards/wnh_wsh/Rules_WSH.pdf",
            Rules = "Almeno 50 dei 100 paesi DXCC dell'emisfero sud elencati nel regolamento. QSO dal 1° gennaio 1985.",
            Key = q => q.Dxcc is { } e && south.Contains(e.Name) ? e.Name : null, Items = Plain(SouthernHemisphere), Map = true,
            Since = new DateTime(1985, 1, 1), Levels = new[] { new AwardLevel("WSH", 50), new AwardLevel("Tutti", 100) },
        });
        var north = dxcc.Where(n => !south.Contains(n)).ToList();
        list.Add(new AwardDef
        {
            Id = "wnh", Name = "Worked Northern Hemisphere (WNH)", Sponsor = "IK3GER", Group = "Paesi DXCC",
            Url = "https://www.ik3ger.it/awards/wnh_wsh/Rules_WNH.pdf",
            Rules = "Almeno 100 dei 240 paesi DXCC dell'emisfero nord (tutti quelli non elencati per l'emisfero sud). QSO dal 1° gennaio 1985.",
            Key = q => q.Dxcc is { } e && !south.Contains(e.Name) ? e.Name : null, Items = Plain(north), Map = true,
            Since = new DateTime(1985, 1, 1), Levels = new[] { new AwardLevel("WNH", 100), new AwardLevel("Tutti", north.Count) },
        });
        list.Add(ListAward("formula1", "Formula I Award", "DG5JFY, Germania", "Paesi DXCC",
            "https://www.qsl.net/va3rj/awards_dx.html",
            "QSO con 20 dei 22 paesi in cui si sono corsi Gran Premi di Formula 1 dal 1980.",
            useWae: false, new[] { new AwardLevel("Formula I", 20), new AwardLevel("Tutti", 22) },
            G("Argentina"), G("Australia"), G("Austria"), G("Belgium"), G("Brazil"), G("Canada"), G("France"),
            G("Germania", "Fed. Rep. of Germany"), G("Gran Bretagna", "England", "Scotland", "Wales", "Northern Ireland"),
            G("Netherlands"), G("Hungary"), G("Italy", "Italy", "Sardinia"), G("Japan"), G("Malaysia", "West Malaysia", "East Malaysia"),
            G("Mexico"), G("Monaco"), G("Portugal"), G("San Marino"), G("South Africa"), G("Spain"), G("Switzerland"), G("United States")));

        // ------------------------------------------------------------ Europa
        list.Add(new AwardDef
        {
            Id = "darc-wae", Name = "WAE - Worked All Europe", Sponsor = "DARC", Group = "Europa",
            Url = "https://www.darc.de/der-club/referate/dx/diplome/wae-diplom/",
            Rules = "Paesi della lista WAE (comprende Sicilia, Shetland, Bear Island, 4U1V e Turchia europea). Un punto per paese " +
                    "per banda, al massimo 5 bande per paese. WAE III: 40 paesi e 100 punti; II: 50 e 150; I: 60 e 200; TOP: 70 e 300.",
            Key = q => q.Wae is { } e && WaeCountries.Contains(e.Name) ? e.Name : null, Items = Plain(WaeCountries), Map = true,
            Score = AwardScore.BandPoints, MaxBandsPerKey = 5, Bands = HfBands,
            Levels = new[] { new AwardLevel("WAE III", 40, 100), new AwardLevel("WAE II", 50, 150), new AwardLevel("WAE I", 60, 200), new AwardLevel("WAE TOP", 70, 300) },
        });
        list.Add(ListAward("darc-waeu", "WA EU - Worked All EU", "DARC", "Europa",
            "https://www.darc.de/der-club/referate/dx/diplome/wa-eu-diplom/",
            "Tutti i 27 paesi dell'Unione Europea, ognuno valido dalla data di adesione. Contano solo i territori pienamente UE.",
            useWae: false, new[] { new AwardLevel("WA EU", 27) },
            Gs("Fed. Rep. of Germany", "France", "Italy", "Luxembourg", "Belgium", "Netherlands", "Ireland", "Denmark", "Greece",
               "Portugal", "Spain", "Austria", "Finland", "Sweden", "Cyprus", "Malta", "Estonia", "Hungary", "Lithuania",
               "Czech Republic", "Slovak Republic", "Slovenia", "Poland", "Latvia", "Bulgaria", "Romania", "Croatia")));
        list.Add(ListAward("raag-europe", "The Europe Award", "RAAG, Grecia", "Europa", "https://raag.org/the-europe-award/",
            "Paesi europei dell'elenco RAAG: categoria minima 40, categoria completa tutti. Bastano i QSO, non servono le QSL.",
            useWae: false, new[] { new AwardLevel("40 paesi", 40), new AwardLevel("Tutti", 73) },
            Gs("Aland Islands", "Albania", "Andorra", "Armenia", "Austria", "Azerbaijan", "Azores", "Balearic Islands", "Belarus",
               "Belgium", "Bosnia-Herzegovina", "Bulgaria", "Corsica", "Crete", "Croatia", "Cyprus", "Czech Republic", "Denmark",
               "Dodecanese", "Estonia", "European Russia", "Faroe Islands", "Finland", "France", "Franz Josef Land", "Georgia",
               "Fed. Rep. of Germany", "Gibraltar", "Greece", "Guernsey", "Hungary", "Iceland", "Ireland", "Isle of Man", "Italy",
               "ITU HQ", "Jan Mayen", "Jersey", "Kaliningrad", "Republic of Kosovo", "Latvia", "Liechtenstein", "Lithuania",
               "Luxembourg", "Malta", "Market Reef", "Moldova", "Monaco", "Montenegro", "Mount Athos", "Netherlands",
               "North Macedonia", "Northern Ireland", "Norway", "Poland", "Portugal", "Romania", "San Marino", "Sardinia",
               "Scotland", "Serbia", "Slovak Republic", "Slovenia", "Sov Mil Order of Malta", "Spain", "Svalbard", "Sweden",
               "Switzerland").Append(G("Turkey", "Asiatic Turkey")).Concat(Gs("Ukraine", "England", "Vatican City", "Wales")).ToArray()));
        list.Add(ListAward("raag-balkan", "The Balkan Countries Award", "RAAG, Grecia", "Europa", "https://raag.org/the-balkan-award/",
            "Due stazioni diverse da ognuno degli 11 paesi balcanici (22 QSO). QSO dal 1° gennaio 1958.",
            useWae: false, new[] { new AwardLevel("Balkan", 11) },
            G("Albania"), G("Bosnia-Herzegovina"), G("Bulgaria"), G("Croatia"), G("Greece", "Greece", "Crete", "Dodecanese", "Mount Athos"),
            G("North Macedonia"), G("Montenegro"), G("Romania"), G("Serbia"), G("Slovenia"), G("Turkey", "Asiatic Turkey")) with
            { CallsPerKey = 2, Since = new DateTime(1958, 1, 1) });
        list.Add(ListAward("ari-cdm", "C.D.M. - Certificato del Mediterraneo", "ARI", "Europa",
            "https://www.ari.it/en/diplomi-hf/diplomiari/1726-cdm-certificato-del-mediterraneo.html",
            "Almeno 24 dei 32 paesi del Mediterraneo dell'elenco ARI (Sicilia compresa), qualunque banda, QSO dal 1° maggio 1945.",
            useWae: true, new[] { new AwardLevel("CDM", 24), new AwardLevel("Tutti", 32) },
            Gs("Monaco", "Tunisia", "Montenegro", "Israel", "Libya", "Cyprus", "Algeria", "Croatia", "Malta", "Morocco", "Palestine",
               "Bosnia-Herzegovina", "Spain", "Balearic Islands", "Ceuta & Melilla", "France", "Italy", "Sardinia", "Sicily", "Lebanon",
               "Slovenia", "Egypt", "Greece", "Dodecanese", "Crete", "Mount Athos").Append(G("Turkey", "Asiatic Turkey", "European Turkey"))
               .Concat(Gs("Corsica", "Syria", "Albania", "Gibraltar", "UK Base Areas on Cyprus")).ToArray()) with { Since = new DateTime(1945, 5, 1) });
        list.Add(ListAward("euro-countries", "Euro Countries", "IK3GER", "Europa", "https://www.ik3ger.it/awards/euro_countries/Rules.pdf",
            "I 19 paesi dell'euro, ognuno su 2 bande diverse (38 QSO), e la frase EURO COUNTRIES composta con l'ultima lettera di " +
            "nominativi italiani (non valgono T7, HV, 1A0 e stranieri in Italia). QSO dal 1° gennaio 2002.",
            useWae: false, new[] { new AwardLevel("Euro Countries", 19) },
            Gs("Austria", "Belgium", "Cyprus", "Estonia", "Finland", "France", "Fed. Rep. of Germany", "Greece", "Ireland", "Italy",
               "Latvia", "Lithuania", "Luxembourg", "Malta", "Netherlands", "Portugal", "Slovak Republic", "Slovenia", "Spain")) with
            { BandsPerKey = 2, Since = new DateTime(2002, 1, 1), Extra = SpellEuroCountries });
        list.Add(ListAward("wanc", "WANC - Worked All Nordic Countries", "IRA, Islanda", "Europa", "https://www.ira.is/vidurkenningarskjol-ira/",
            "Paesi nordici, l'Islanda (TF) è obbligatoria. Classe C: 5 paesi (1 in zona CQ 40); B: 8 (2 in zona 40); A: tutti gli 11. " +
            "Tutti i QSO nello stesso modo; i nominativi /TF e /OX non valgono.",
            useWae: false, new[] { new AwardLevel("Classe C", 5), new AwardLevel("Classe B", 8), new AwardLevel("Classe A", 11) },
            Gs("Svalbard", "Jan Mayen", "Norway", "Finland", "Aland Islands", "Market Reef", "Greenland", "Faroe Islands", "Denmark",
               "Sweden", "Iceland")) with { Required = new[] { "Iceland" } });
        list.Add(ListAward("ira-zone40", "Zone 40 Award", "IRA, Islanda", "Europa", "https://www.ira.is/vidurkenningarskjol-ira/",
            "Tutti i paesi della zona CQ 40: Islanda, Groenlandia, Jan Mayen, Svalbard, Terra di Francesco Giuseppe. Per le stazioni " +
            "europee anche 3 stazioni islandesi diverse. Tutti i QSO nello stesso modo.",
            useWae: false, new[] { new AwardLevel("Zone 40", 5) },
            Gs("Iceland", "Greenland", "Jan Mayen", "Svalbard", "Franz Josef Land")) with { Extra = ThreeIcelanders });
        list.Add(ListAward("oevsv-alpine", "Alpine Country Award", "ÖVSV, Austria", "Europa", "https://www.oevsv.at/opencms/oevsv/diplome/",
            "5 stazioni diverse da ognuno di 7 degli 8 paesi alpini. QSO dal 1° gennaio 2010, tutte le bande e i modi.",
            useWae: false, new[] { new AwardLevel("Alpine", 7), new AwardLevel("Tutti", 8) },
            G("France"), G("Monaco"), G("Switzerland"), G("Liechtenstein"), G("Italy", "Italy", "Sardinia"),
            G("Germania", "Fed. Rep. of Germany"), G("Austria"), G("Slovenia")) with { CallsPerKey = 5, Since = new DateTime(2010, 1, 1) });

        // ------------------------------------------------------------ Continenti e aree
        list.Add(new AwardDef
        {
            Id = "wac", Name = "WAC - Worked All Continents", Sponsor = "IARU (DARC, URE, WIA, VRZA)", Group = "Continenti e aree",
            Url = "https://www.darc.de/der-club/referate/dx/diplome/wac-diplom/",
            Rules = "Un QSO con ognuno dei sei continenti, qualunque banda e modo.",
            Key = q => q.Dxcc?.Continent, Items = Continents.Select(c => new AwardItem(c.Key, c.Value, Array.Empty<string>())).ToList(),
            Levels = new[] { new AwardLevel("WAC", 6) },
        });
        list.Add(new AwardDef
        {
            Id = "5bwac", Name = "5BWAC - WAC 5 bande", Sponsor = "URE, DARC", Group = "Continenti e aree",
            Url = "https://diplomas.ure.es/diplomas/wac-worked-all-continents/bases/",
            Rules = "I sei continenti su ognuna delle bande 80, 40, 20, 15 e 10 m (30 QSO). Le bande WARC non valgono.",
            Key = q => q.Dxcc?.Continent, Score = AwardScore.BestBands, Bands = FiveBands, BandCount = 5, PerBand = 6,
            Levels = new[] { new AwardLevel("5BWAC", 30) },
        });
        list.Add(new AwardDef
        {
            Id = "rca-ccc", Name = "CCC - Cinco Continentes Comunicados", Sponsor = "Radio Club Argentino", Group = "Continenti e aree",
            Url = "https://www.qsl.net/lu5gpl/saltos/awards.htm",
            Rules = "America, Africa, Asia, Oceania ed Europa, ognuno su due bande diverse.",
            Key = q => q.Dxcc?.Continent switch { "NA" or "SA" => "AM", null => null, var c => c },
            Items = new[] { ("AM", "America"), ("AF", "Africa"), ("AS", "Asia"), ("OC", "Oceania"), ("EU", "Europa") }
                .Select(c => new AwardItem(c.Item1, c.Item2, Array.Empty<string>())).ToList(),
            BandsPerKey = 2, Since = new DateTime(1945, 11, 20), Levels = new[] { new AwardLevel("CCC", 5) },
        });
        var asia = cty.Entities.Where(e => e.Continent == "AS").Select(e => e.Name).ToList();
        list.Add(new AwardDef
        {
            Id = "jarl-adxa", Name = "ADXA - Asian DX Award", Sponsor = "JARL", Group = "Continenti e aree",
            Url = "https://www.jarl.org/English/4_Library/A-4-2_Awards/Aw_adxa.htm",
            Rules = "Entità DXCC dell'Asia, Giappone compreso: 15 per l'ADXA-Half, 30 per l'ADXA.",
            Key = q => q.Dxcc is { Continent: "AS" } e ? e.Name : null, Items = Plain(asia), Map = true, Required = new[] { "Japan" },
            Levels = new[] { new AwardLevel("ADXA-Half", 15), new AwardLevel("ADXA", 30) },
        });
        var africa = cty.Entities.Where(e => e.Continent == "AF").Select(e => e.Name).ToList();
        list.Add(new AwardDef
        {
            Id = "waac", Name = "W.A.A.C. - Worked All African Continent", Sponsor = "IK7NXM", Group = "Continenti e aree",
            Url = "https://sites.google.com/site/ik7nxm/",
            Rules = "40 paesi africani della lista DXCC in HF (6-160 m), QSO dal 1° gennaio 1980; targa con 70.",
            Key = q => q.Dxcc is { Continent: "AF" } e ? e.Name : null, Items = Plain(africa), Map = true,
            Since = new DateTime(1980, 1, 1), Levels = new[] { new AwardLevel("W.A.A.C.", 40), new AwardLevel("Targa", 70) },
        });
        list.Add(ListAward("wacs", "WACS - Worked All Caribbean Sea", "IK7NXU", "Continenti e aree", "https://www.qsl.net/ik7nxu/wacs.html",
            "Almeno 33 dei 46 paesi del Mar dei Caraibi dell'elenco, in HF da 160 a 10 m.",
            useWae: false, new[] { new AwardLevel("WACS", 33), new AwardLevel("Tutti", 46) },
            Gs("Jamaica", "Barbados", "Trinidad & Tobago", "Bahamas", "Cuba", "Guadeloupe", "Martinique", "St. Martin", "Haiti",
               "Dominican Republic", "Colombia", "Malpelo Island", "San Andres & Providencia", "Panama", "Honduras", "Grenada",
               "St. Lucia", "Dominica", "St. Vincent", "Guantanamo Bay", "Navassa Island", "US Virgin Islands", "Puerto Rico",
               "Desecheo Island", "Aruba", "Guatemala", "Costa Rica", "Cocos Island", "Antigua & Barbuda", "Belize", "St. Kitts & Nevis",
               "Anguilla", "Montserrat", "British Virgin Islands", "Turks & Caicos Islands", "Mexico", "Revillagigedo", "Nicaragua",
               "Venezuela", "Aves Island", "Cayman Islands", "Curacao", "Bonaire", "Saba & St. Eustatius", "Sint Maarten",
               "St. Barthelemy")) with { Bands = HfBands });
        list.Add(ListAward("rca-tpa", "TPA - Todos los Países de América", "Radio Club Argentino", "Continenti e aree",
            "https://www.qsl.net/lu5gpl/saltos/awards.htm",
            "I 22 paesi americani dell'elenco, qualunque banda e modo.",
            useWae: false, new[] { new AwardLevel("TPA", 22) },
            Gs("Chile", "Cuba", "Bolivia", "Uruguay", "Ecuador", "Haiti", "Dominican Republic", "Colombia", "Panama", "Honduras",
               "Argentina", "Peru", "Brazil", "Guatemala", "Costa Rica", "Canada", "United States", "Mexico", "Nicaragua",
               "El Salvador", "Venezuela", "Paraguay")) with { Since = new DateTime(1945, 11, 20) });

        // ------------------------------------------------------------ Isole
        list.Add(new AwardDef
        {
            Id = "iota", Name = "IOTA - Islands On The Air", Sponsor = "IOTA Ltd (iota-world.org)", Group = "Isole",
            Url = "https://www.iota-world.org/",
            Rules = "Gruppi di isole IOTA diversi (campo IOTA del log). Il diploma base è a 100 riferimenti, poi ogni 100. " +
                    "Le classi e le conferme valide vanno controllate su iota-world.org.",
            Key = q => q.Entry.Iota.Length > 0 ? q.Entry.Iota : null,
            Levels = Steps(100, 700, 100, "IOTA ").Append(new AwardLevel("IOTA 750", 750)).ToArray(),
        });

        // ------------------------------------------------------------ Stazioni di un paese
        list.Add(Stations("w100i", "W-100-I", "IK3GER", "https://www.ik3ger.it/awards/w100i/Rules.pdf",
            "Stazioni italiane diverse (anche speciali) che operano dall'Italia. Chi opera in portatile conta come il nominativo base; " +
            "non valgono stranieri in Italia, T7, HV, 1A0. QSO dal 1° gennaio 1985.",
            ItalyEntities, new[] { new AwardLevel("Classe 4", 100), new AwardLevel("Classe 3", 250), new AwardLevel("Classe 2", 500), new AwardLevel("Classe 1", 1000) },
            since: new DateTime(1985, 1, 1)));
        list.Add(Stations("waoe100", "WAOE 100 HF", "ÖVSV, Austria", "https://www.oevsv.at/opencms/oevsv/diplome/",
            "100 stazioni austriache diverse in HF.", new[] { "Austria" }, new[] { new AwardLevel("WAOE 100", 100) }, bands: HfBands));
        list.Add(Stations("w100om", "W 100 OM", "SZR, Slovacchia", "http://www.hamradio.sk/diplomy/nase_dip.htm",
            "100 stazioni slovacche diverse, QSO dal 1° gennaio 1993; bollini da 200 a 700, poi 850 e 1000. Non valgono i ripetitori.",
            new[] { "Slovak Republic" }, Steps(100, 700, 100, "W ").Append(new AwardLevel("W 850", 850)).Append(new AwardLevel("W 1000", 1000)).ToArray(),
            since: new DateTime(1993, 1, 1)));
        list.Add(Stations("la100", "LA 100 Award", "NRRL Jærgruppen, Norvegia", "https://www.la1j.no/info/la100-award/",
            "100 stazioni norvegesi diverse, QSO dal 1° gennaio 1984.", new[] { "Norway" }, new[] { new AwardLevel("LA 100", 100) },
            since: new DateTime(1984, 1, 1)));
        list.Add(Stations("yv100", "Diploma YV 100 / 200 / 300", "Radio Club Venezolano", "https://radioyv.club/diplomayv100.php",
            "100, 200 o 300 stazioni venezuelane diverse, qualunque banda e modo.", new[] { "Venezuela" },
            new[] { new AwardLevel("YV 100", 100), new AwardLevel("YV 200", 200), new AwardLevel("YV 300", 300) }));
        list.Add(Stations("zp100", "ZP 100 … ZP 500", "Radio Club Paraguayo", "https://www.qsl.net/zp5yw/zpaward.htm",
            "Da 100 a 500 stazioni paraguaiane diverse (un diploma ogni 50).", new[] { "Paraguay" }, Steps(100, 500, 50, "ZP ").ToArray()));

        // ------------------------------------------------------------ Prefissi
        list.Add(Prefixes("eu-px-a", "EU-PX-A - European Prefixes Award", "DIG, Germania",
            "https://diplom-interessen-gruppe.info/dig-diplome/eu-px-a/",
            "100 prefissi europei diversi secondo le regole WPX (anche i prefissi speciali), QSL dal 1° gennaio 1969; poi ogni 50.",
            q => q.Dxcc?.Continent == "EU", Steps(100, 500, 50).ToArray(), since: new DateTime(1969, 1, 1)));
        list.Add(Prefixes("prefixes-sp", "Prefixes-SP", "PZK, Polonia", "https://awards.pzk.org.pl/polish-awards/prefixes-sp.html",
            "80 prefissi polacchi diversi in HF (SP, SQ, SN, SO, SR, HF, 3Z e prefissi speciali).",
            q => q.Dxcc?.Name == "Poland", new[] { new AwardLevel("Prefixes-SP", 80) }, bands: HfBands));
        list.Add(Prefixes("ct-wpx", "CT-WPX Portugal Prefix Award", "CT1BWW", "http://www.ct1bww.com/ctwpxaward.htm",
            "10 prefissi diversi del Portogallo, di Madeira e delle Azzorre; targa con 30.",
            q => q.Dxcc?.Name is "Portugal" or "Madeira Islands" or "Azores",
            new[] { new AwardLevel("CT-WPX", 10), new AwardLevel("Targa", 30) }));

        return list;
    }

    // ------------------------------------------------------------ costruttori

    private static IReadOnlyList<AwardItem> Plain(IEnumerable<string> names) =>
        names.Select(n => new AwardItem(n, n, new[] { n })).ToList();

    private static AwardItem G(string name) => new(name, name, new[] { name });
    private static AwardItem G(string label, params string[] entities) => new(label, label, entities);
    private static AwardItem[] Gs(params string[] names) => names.Select(G).ToArray();

    private static IEnumerable<AwardLevel> Steps(int from, int to, int step, string prefix = "") =>
        Enumerable.Range(0, (to - from) / step + 1).Select(i => from + i * step).Select(n => new AwardLevel($"{prefix}{n}", n));

    private static AwardDef ListAward(string id, string name, string sponsor, string group, string url, string rules,
        bool useWae, AwardLevel[] levels, params AwardItem[] items)
    {
        var map = items.SelectMany(i => i.Entities.Select(e => (e, i.Key))).ToDictionary(p => p.e, p => p.Key, StringComparer.OrdinalIgnoreCase);
        return new AwardDef
        {
            Id = id, Name = name, Sponsor = sponsor, Group = group, Url = url, Rules = rules, Levels = levels, Items = items, Map = true,
            Key = q => (useWae ? q.Wae : q.Dxcc)?.Name is { } n && map.TryGetValue(n, out var k) ? k : null,
        };
    }

    private static AwardDef Stations(string id, string name, string sponsor, string url, string rules, string[] entities,
        AwardLevel[] levels, string[]? bands = null, DateTime? since = null)
    {
        var set = new HashSet<string>(entities);
        return new AwardDef
        {
            Id = id, Name = name, Sponsor = sponsor, Group = "Stazioni di un paese", Url = url, Rules = rules, Levels = levels,
            Bands = bands, Since = since,
            // Stranieri che operano dal paese (IS0/DL5AA) non valgono: conta il paese del nominativo base.
            Key = q => q.Dxcc is { } e && set.Contains(e.Name) && (q.Home is null || set.Contains(q.Home.Name)) ? Callsign.Base(q.Call) : null,
        };
    }

    private static AwardDef Prefixes(string id, string name, string sponsor, string url, string rules,
        Func<AwardQso, bool> where, AwardLevel[] levels, string[]? bands = null, DateTime? since = null) => new()
    {
        Id = id, Name = name, Sponsor = sponsor, Group = "Prefissi", Url = url, Rules = rules, Levels = levels, Bands = bands, Since = since,
        Key = q => where(q) ? Callsign.WpxPrefix(q.Call) : null,
    };

    // ------------------------------------------------------------ requisiti aggiuntivi

    /// <summary>"EURO COUNTRIES" con l'ultima lettera di nominativi italiani diversi.</summary>
    public static ExtraCheck SpellEuroCountries(IReadOnlyList<AwardQso> qsos)
    {
        var lastLetters = qsos.Where(q => q.Dxcc is { } e && ItalyEntities.Contains(e.Name) && (q.Home is null || ItalyEntities.Contains(q.Home.Name)))
            .Select(q => Callsign.Base(q.Call)).Distinct(StringComparer.OrdinalIgnoreCase)
            .GroupBy(c => c[^1]).ToDictionary(g => g.Key, g => g.Count());
        var missing = "EUROCOUNTRIES".GroupBy(c => c)
            .SelectMany(g => Enumerable.Repeat(g.Key, Math.Max(0, g.Count() - lastLetters.GetValueOrDefault(g.Key)))).ToList();
        return missing.Count == 0
            ? new(true, "Frase EURO COUNTRIES composta con le ultime lettere di nominativi italiani.")
            : new(false, "Frase EURO COUNTRIES: mancano nominativi italiani che finiscono con " + string.Join(", ", missing) + ".");
    }

    private static ExtraCheck ThreeIcelanders(IReadOnlyList<AwardQso> qsos)
    {
        var n = qsos.Where(q => q.Dxcc?.Name == "Iceland").Select(q => Callsign.Base(q.Call)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        return new(n >= 3, $"Stazioni islandesi diverse: {n} su 3 richieste (per le stazioni europee).");
    }
}
