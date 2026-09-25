using System;
using System.IO;
using System.Collections.Generic;
using KeepersJournal;

class Program
{
    static int count;
    static void Check(bool passed, string description) { count++; if (!passed) throw new Exception(description); }
    static void Main()
    {
        string dir = Path.Combine(Path.GetTempPath(), "keeper-localization-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var warnings = new List<string>();
            var catalog = new TranslationCatalog();
            File.WriteAllText(Path.Combine(dir,"en.json"), "{\"Title\":\"English\",\"Only English\":\"Fallback\"}");
            File.WriteAllText(Path.Combine(dir,"fr.json"), "{\"Title\":\"Matériaux\",\"Count {0} / {1}\":\"Besoin {1}, disponible {0}\",\"Broken {0}\":\"Cassé\",\"Blank\":\"\",\"Not text\":42}");
            File.WriteAllText(Path.Combine(dir,"fr_ca.json"), "{\"Title\":\"Québec\"}");
            catalog.Load(dir,"fr-CA",warnings.Add);
            Check(catalog.Get("Title")=="Québec","regional language priority");
            Check(catalog.Format("Count {0} / {1}",2,5)=="Besoin 5, disponible 2","neutral fallback, reorder placeholders");
            Check(catalog.Get("Only English")=="Fallback","English file fallback");
            Check(catalog.Get("Missing")=="Missing","built-in fallback");
            Check(catalog.Format("Broken {0}",4)=="Broken 4","bad placeholders fall back");
            Check(catalog.Get("Blank")=="Blank" && catalog.Get("Not text")=="Not text","bad entries fall back");
            Check(warnings.Count==3,"bad entries diagnosed");
            Check(!TranslationCatalog.ValidFormat("Count {0}","Count {0"),"broken formatting rejected");
            Check(!TranslationCatalog.ValidFormat("Count {0}","Count {1}"),"wrong argument rejected");
            Check(TranslationCatalog.Normalize("KR")=="ko","Korean alias");
            Check(TranslationCatalog.Normalize("../../private")=="en","path traversal rejected");
            File.WriteAllText(Path.Combine(dir,"ko.json"),"{\"Title\":\"재료\"}");
            catalog.Load(dir,"kor",warnings.Add);
            Check(catalog.Get("Title")=="재료","Korean UTF-8");
            File.WriteAllText(Path.Combine(dir,"de.json"),"{bad JSON");
            catalog.Load(dir,"de",warnings.Add);
            Check(catalog.Get("Title")=="English","malformed file fallback");
            catalog.Load(dir,"ja",warnings.Add);
            Check(catalog.Get("Title")=="English","language switch removes old overrides");
            var days = new Dictionary<string,int> { {"day_envy",4} };
            var tasks = JournalRules.MorningLines(new[]{"day_wrath"},0,days,true,false,false,false,false);
            Check(tasks.Count==1 && tasks[0]=="sermon","daily completion uses stable IDs");
            catalog.Load(dir,"fr",warnings.Add);
            Check(JournalRules.MorningLines(new[]{"day_wrath"},0,days,true,false,false,false,false)[0]==tasks[0],"translation cannot change daily task identity");
            Console.WriteLine(count+" localization checks passed.");
        }
        finally { Directory.Delete(dir,true); }
    }
}
