using System;
using System.Collections.Generic;
using KeepersJournal;
class RuleTests
{
    static int checks;
    static void Check(bool result, string label) { checks++; if (!result) throw new Exception(label); }
    static void Main()
    {
        Check(JournalRules.KnownScriptResult("kitchen_oven_up_s")=="kitchen_oven_t2", "Oven result mapping");
        Check(JournalRules.KnownScriptResult("kitchen_table_up_s")=="kitchen_table_t2", "Table result mapping");
        Check(JournalRules.KnownScriptResult("zombie_supplier_station_house_s")=="zombie_supplier_station_mini", "Station result mapping");
        Check(JournalRules.KnownScriptResult("unknown_script")==null, "Do not guess unknown script results");
        var objects = new List<BuildingFact> {
            new BuildingFact {Id="bench",Zone="yard"}, new BuildingFact {Id="bench",Zone="yard"},
            new BuildingFact {Id="bench_place",Zone="yard"}, new BuildingFact {Id="bench",Zone="other"},
            new BuildingFact {Id="bench_2",Zone="yard"}, new BuildingFact {Id="bench",Zone="yard",Hidden=true},
            new BuildingFact {Id="bench",Zone="yard",Temporary=true}, new BuildingFact {Id="bench",Zone="yard",Removing=true,Present=false}
        };
        var plots = new List<BuildingFact> {
            new BuildingFact {Id="garden_empty",Zone="garden",Group="garden_bed"},
            new BuildingFact {Id="crop_growing_fixture",Zone="garden",Group="garden_bed"},
            new BuildingFact {Id="crop_ready_fixture",Zone="garden",Group="garden_bed"},
            new BuildingFact {Id="garden_empty_place",Zone="garden",Group="garden_bed"},
            new BuildingFact {Id="garden_empty",Zone="other",Group="garden_bed"},
            new BuildingFact {Id="hidden_crop_fixture",Zone="garden",Group="garden_bed",Hidden=true},
            new BuildingFact {Id="deleted_crop_fixture",Zone="garden",Group="garden_bed",Present=false},
            new BuildingFact {Id="preview_crop_fixture",Zone="garden",Group="garden_bed",Temporary=true},
            new BuildingFact {Id="garden_sign",Zone="garden",Group="signs"},
            new BuildingFact {Id="vine_crop_fixture",Zone="garden",Group="vineyard_objects"}
        };
        var plotCount=JournalRules.CountBuildings(plots,"garden","garden_empty","garden_empty_place");
        Check(plotCount.Built==3 && plotCount.InProgress==1,"Garden count includes growing/ready plots but excludes construction and unrelated objects");
        plots[0].Id="planted_fixture";
        Check(JournalRules.CountBuildings(plots,"garden","garden_empty","garden_empty_place").Built==3,"Planting must not reduce bed count");
        plots[1].Id="garden_empty";
        Check(JournalRules.CountBuildings(plots,"garden","garden_empty","garden_empty_place").Built==3,"Harvesting must not change bed count");
        Check(JournalRules.CountBuildings(plots,"garden","vineyard_empty","vineyard_empty_place").Built==1,"Vineyard beds remain separate from garden beds");
        Check(JournalRules.CountBuildings(plots,"garden","garden_sign","garden_sign_place").Built==1,"Other recipes retain exact counts");
        var count = JournalRules.CountBuildings(objects,"yard","bench","bench_place");
        Check(count.Built==2 && count.InProgress==1,"Area/type filtering or construction count failed");
        Check(JournalRules.CountBuildings(objects,"other","bench","bench_place").Built==1,"Counts leaked between areas");
        Check(JournalRules.CountBuildings(objects,"yard","missing","missing").Built==0,"Unbuilt blueprint must show zero");
        Check(JournalRules.CountBuildings(objects,"yard","bench","bench").InProgress==0,"Instant build counted as construction");
        objects.RemoveAt(0);
        Check(JournalRules.CountBuildings(objects,"yard","bench","bench_place").Built==1,"Demolished building still counted");
        Check(JournalRules.CountBuildings(objects,"yard","bench_2","bench_2").Built==1,"Upgrade tier must remain distinct");
        var staleFlag = new List<BuildingFact> { new BuildingFact {Id="firewood_shed_kitchen_1",Zone="home",Removing=true,Present=true} };
        Check(JournalRules.CountBuildings(staleFlag,"home","firewood_shed_kitchen_1","firewood_shed_kitchen_1_place").Built==1,"Regression: constructed house shed retains old removal flag");
        staleFlag[0].Present=false;
        Check(JournalRules.CountBuildings(staleFlag,"home","firewood_shed_kitchen_1","firewood_shed_kitchen_1_place").Built==0,"Actually removed shed must be excluded");
        Check(JournalRules.KnownScriptResult("millstone_s")=="millstone","Millstone script result");
        Check(JournalRules.KnownScriptResult("home_attic_s")=="ladder_home_first_floor","Attic script result");
        Check(JournalRules.KnownScriptResult("customization_enable_s")=="player_customization_after_fix","Dresser script result");
        var days = new Dictionary<string,int>{{"day_pride",1},{"day_lust",2},{"day_gluttony",3},{"day_envy",4},{"day_wrath",5},{"day_sloth",6}};
        for(int day=1;day<=6;day++)
        {
            for(int flags=0;flags<64;flags++)
                Check(JournalRules.MorningLines(new string[0],day,days,(flags&1)!=0,(flags&2)!=0,(flags&4)!=0,(flags&8)!=0,(flags&16)!=0).Count==0,
                    "Unknown activity revealed on day "+day);
        }
        Check(JournalRules.MorningLines(new[]{"day_wrath"},5,days,true,false,false,false,false).Count==1,"Unlocked sermon missing");
        Check(JournalRules.MorningLines(new[]{"day_pride"},5,days,true,false,false,false,false).Count==0,"Wrong calendar gate revealed sermon");
        Check(JournalRules.MorningLines(new[]{"day_wrath"},5,days,false,false,false,false,false).Count==0,"Unavailable sermon announced");
        Check(JournalRules.MorningLines(new[]{"day_pride"},1,days,false,false,true,false,false).Count==0,"Unvisited board revealed");
        Check(JournalRules.MorningLines(new[]{"day_pride"},1,days,false,true,true,false,false).Count==1,"Known board missing");
        Check(JournalRules.MorningLines(new[]{"day_envy"},4,days,false,false,false,false,false).Count==0,"Unpowered equipment announced");
        Check(JournalRules.MorningLines(new[]{"day_lust"},1,days,false,false,false,false,false).Count==0,"Calendar event announced on wrong day");
        var morning = new MorningTracker();
        morning.Load(40,0.1f);
        Check(!morning.IsDue(40,0.24f),"Midnight is not dawn");
        Check(morning.IsDue(40,0.25f),"Dawn reminder missing");
        morning.MarkShown(40);
        Check(!morning.IsDue(40,0.3f),"Duplicate morning reminder");
        Check(!morning.IsDue(41,0.1f),"Next day too early");
        Check(morning.IsDue(41,0.25f),"Next morning missing");
        morning.Load(41,0.4f);
        Check(!morning.IsDue(41,0.41f),"Loading a save repeated the morning");
        morning.Reset(); morning.Load(1,0.1f);
        Check(morning.IsDue(1,0.25f),"Save-switch state leaked");
        Check(!morning.IsDue(1,0.85f),"Stale reminder after nightfall");
        Console.WriteLine("PASS: "+checks+" checks (build-menu counts, spoiler gates, dawn, duplicates, and save switching).");
    }
}
