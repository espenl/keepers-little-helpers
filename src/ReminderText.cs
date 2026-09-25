namespace KeepersJournal { internal static class ReminderText { internal static string Get(string key) { switch(key) {
case "sermon": return L.T("I can give a sermon today. Better remember my prayer!");
case "board": return L.T("The order board is open today. I should check it.");
case "battle": return L.T("The battle window is open today. I should check my preparations.");
case "storm": return L.T("A storm day. I should check the resurrection equipment.");
case "arrivals": return L.T("Today's the day to check for new arrivals.");
case "panic": return L.T("I should check the panic-reduction machine today.");
case "quiet": return L.T("Nothing special on my calendar today. Back to work!");
default: return key; } } } }
