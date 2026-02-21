namespace Eco.Mods.TechTree
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Reflection;
    using System.IO;
    using Eco.Core.Plugins.Interfaces;
    using Eco.Gameplay.Items;
    using Eco.Gameplay.Players;
    using Eco.Gameplay.Systems.Messaging.Chat.Commands;
    using Eco.Gameplay.Systems.TextLinks;
    using Eco.Shared.Localization;
    using Eco.Shared.Math;
    using Eco.Shared.Utils;
    using Eco.Core.Utils;

    public class DietPlan
    {
        public Dictionary<string, int> Foods { get; set; } = new Dictionary<string, int>();
        public double Score { get; set; }
        public float TotalCalories { get; set; }
        public float Carbs { get; set; }
        public float Fat { get; set; }
        public float Protein { get; set; }
        public float Vitamins { get; set; }
        public float AverageTier { get; set; }
        public float AverageLevel { get; set; }
        public float AverageTasteScore { get; set; }
    }

    public class DietResult
    {
        public DateTime GeneratedAt { get; set; }
        public DietPlan Plan { get; set; }
    }

    [ChatCommandHandler]
    public class EcoDietOptimizer : IModKitPlugin, IInitializablePlugin
    {
        public string GetStatus() => "Active";
        public string GetCategory() => "User";

        private static string CacheFilePath = "EcoDietOptimizer_Cache.txt";
        private static string LogFilePath = "EcoDietOptimizer_Log.txt";
        private static Dictionary<string, DietResult> DietCache = new Dictionary<string, DietResult>();
        private static readonly object _lock = new object();
        private static Random rng = new Random();
        private static int CooldownMinutes = 1440;
        private static bool DebugMode = false;
        private static bool StrictMode = true;

        public void Initialize(TimedTask timer)
        {
            LoadData();
        }

        private static void Log(string message)
        {
            if (!DebugMode) return;
            try
            {
                lock(_lock)
                {
                    File.AppendAllText(LogFilePath, $"{DateTime.Now}: {message}{Environment.NewLine}");
                }
            }
            catch {}
        }

        private static void LoadData()
        {
            lock(_lock)
            {
                try
                {
                    if (File.Exists(CacheFilePath))
                    {
                        var lines = File.ReadAllLines(CacheFilePath);
                        foreach(var line in lines)
                        {
                            var parts = line.Split(new[] { ";;" }, StringSplitOptions.None);
                            if (parts.Length < 9) continue;

                            string userId = parts[0];
                            long ticks = long.Parse(parts[1]);
                            var foods = new Dictionary<string, int>();
                            foreach(var f in parts[2].Split(','))
                            {
                                var fp = f.Split(':');
                                if (fp.Length == 2) foods[fp[0]] = int.Parse(fp[1]);
                            }

                            var plan = new DietPlan
                            {
                                Foods = foods,
                                Score = double.Parse(parts[3]),
                                TotalCalories = float.Parse(parts[4]),
                                Carbs = float.Parse(parts[5]),
                                Fat = float.Parse(parts[6]),
                                Protein = float.Parse(parts[7]),
                                Vitamins = float.Parse(parts[8])
                            };

                            // Attempt to parse new fields if available, otherwise default
                            float tier = 0;
                            if (parts.Length > 9) float.TryParse(parts[9], out tier);
                            plan.AverageTier = tier;

                            float level = 0;
                            if (parts.Length > 10) float.TryParse(parts[10], out level);
                            plan.AverageLevel = level;

                            DietCache[userId] = new DietResult { GeneratedAt = new DateTime(ticks), Plan = plan };
                        }
                    }
                }
                catch { }
            }
        }

        private static void SaveData()
        {
            lock(_lock)
            {
                try
                {
                    var lines = new List<string>();
                    foreach(var kvp in DietCache)
                    {
                        var r = kvp.Value;
                        var p = r.Plan;
                        string foodStr = string.Join(",", p.Foods.Select(f => $"{f.Key}:{f.Value}"));
                        lines.Add($"{kvp.Key};;{r.GeneratedAt.Ticks};;{foodStr};;{p.Score};;{p.TotalCalories};;{p.Carbs};;{p.Fat};;{p.Protein};;{p.Vitamins};;{p.AverageTier};;{p.AverageLevel}");
                    }
                    File.WriteAllLines(CacheFilePath, lines);
                }
                catch { }
            }
        }

        [ChatCommand("Suggests an optimal diet based on your stomach size and tastes.", "diet")]
        public static void SuggestDiet(User user, string arg = "")
        {
            try
            {
                if (string.IsNullOrWhiteSpace(arg))
                {
                    HandleDietRequest(user, 0);
                    return;
                }

                if (int.TryParse(arg, out int meals))
                {
                    HandleDietRequest(user, meals);
                    return;
                }

                switch (arg.ToLower())
                {
                    case "clear":
                        lock(_lock)
                        {
                            if (DietCache.ContainsKey(user.Name))
                            {
                                DietCache.Remove(user.Name);
                                SaveData();
                                user.Player.MsgLocStr("Diet cache cleared.");
                            }
                            else
                            {
                                user.Player.MsgLocStr("No cached diet found.");
                            }
                        }
                        break;
                    case "debug":
                        if (!user.IsAdmin) { user.Player.MsgLocStr("Permission Denied: This command is for admins only."); return; }
                        DebugMode = !DebugMode;
                        user.Player.MsgLocStr($"Debug mode is now {(DebugMode ? "ON" : "OFF")}.");
                        Log("Debug mode enabled via chat.");
                        break;
                    case "strict":
                        StrictMode = !StrictMode;
                        user.Player.MsgLocStr($"Strict Discovery Mode (Only Tasted Foods) is now {(StrictMode ? "ON" : "OFF")}.");
                        break;
                    case "probe":
                        ProbeReflection(user);
                        break;
                    case "taste":
                        ShowTasteList(user);
                        break;
                    case "help":
                        ShowHelp(user);
                        break;
                    default:
                        if (arg.StartsWith("config "))
                        {
                            if (!user.IsAdmin) { user.Player.MsgLocStr("Permission Denied: This command is for admins only."); return; }
                            var parts = arg.Split(' ');
                            if (parts.Length > 1 && int.TryParse(parts[1], out int mins))
                            {
                                CooldownMinutes = mins;
                                user.Player.MsgLocStr($"Diet cooldown set to {CooldownMinutes} minutes (Global).");
                            }
                            else
                            {
                                user.Player.MsgLocStr("Usage: /diet config <minutes>");
                            }
                        }
                        else
                        {
                            user.Player.MsgLocStr("Usage: /diet [meals | clear | strict | taste | help]");
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                user.Player.MsgLocStr($"Error: {ex.Message}");
                Log($"Error in SuggestDiet: {ex}");
            }
        }

        private static void ShowHelp(User user)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<b>Eco Diet Optimizer</b>");
            sb.AppendLine("This mod helps you maximize your skill gain by suggesting the best balanced diet based on your stomach size and known food preferences. It prioritizes balanced nutrition (25% each of Carbs, Fat, Protein, Vitamins).");
            sb.AppendLine();
            sb.AppendLine("<b>User Commands:</b>");
            sb.AppendLine("- <b>/diet</b>: Suggests the best balanced diet for 1 meal (Uses only tasted foods by default).");
            sb.AppendLine("- <b>/diet N</b>: Generates a shopping list for N meals based on the current suggestion.");
            sb.AppendLine("- <b>/diet taste</b>: Lists your discovered foods grouped by taste preference.");
            sb.AppendLine("- <b>/diet clear</b>: Clears the currently cached diet suggestion, forcing a recalculation.");
            sb.AppendLine("- <b>/diet strict</b>: Toggles strict discovery mode (On: Only known/tasted foods. Off: Includes all foods).");
            sb.AppendLine();
            sb.AppendLine("<b>Admin Commands:</b>");
            sb.AppendLine("- <b>/diet config N</b>: Sets the global cooldown period for diet recalculation to N minutes.");
            sb.AppendLine("- <b>/diet debug</b>: Toggles verbose logging to 'EcoDietOptimizer_Log.txt'.");

            user.Player.MsgLocStr(sb.ToString());
        }

        private static string GenerateTasteListString(User user, bool richText)
        {
            // Key: Preference (String), Value: List of Food Names
            var groupedFoods = GetGroupedFoodTypesFromTasteBuds(user);
            bool favDiscovered = IsFavoriteDiscovered(user);
            bool worstDiscovered = IsWorstDiscovered(user);

            StringBuilder sb = new StringBuilder();
            int totalCount = 0;

            // Colors
            string cFavDel = "#00ff00";
            string cGood   = "#7fff7f";
            string cOk     = "#7f7f7f";
            string cBad    = "#ff7f7f";
            string cHorr   = "#f50000";

            string ColorText(string text, string hex) => richText ? $"<color={hex}>{text}</color>" : text;
            string Bold(string text) => richText ? $"<b>{text}</b>" : text;

            // Favorite
            string favName = "Unknown";
            if (favDiscovered && groupedFoods.ContainsKey("Favorite") && groupedFoods["Favorite"].Any())
            {
                favName = groupedFoods["Favorite"].First(); // Should be item link now from helper
                totalCount += groupedFoods["Favorite"].Count;
            }
            sb.AppendLine($"{Bold(ColorText("Favorite", cFavDel))}: {favName}");

            // Worst
            string worstName = "Unknown";
            if (worstDiscovered && groupedFoods.ContainsKey("Worst") && groupedFoods["Worst"].Any())
            {
                worstName = groupedFoods["Worst"].First();
                totalCount += groupedFoods["Worst"].Count;
            }
            sb.AppendLine($"{Bold(ColorText("Worst", cHorr))}: {worstName}");

            // Group Output Helper
            void AppendGroup(string key, string colorHex)
            {
                if (groupedFoods.ContainsKey(key) && groupedFoods[key].Count > 0)
                {
                    sb.AppendLine($"--- {Bold(ColorText(key, colorHex))} ---");
                    foreach(var food in groupedFoods[key])
                    {
                        sb.AppendLine($"- {food}");
                        totalCount++;
                    }
                }
            }

            AppendGroup("Delicious", cFavDel);
            AppendGroup("Good", cGood);
            AppendGroup("Ok", cOk);
            AppendGroup("Bad", cBad);
            AppendGroup("Horrible", cHorr);

            sb.AppendLine();
            sb.AppendLine($"Total of known foods: {totalCount}");

            return sb.ToString();
        }

        private static void ShowTasteList(User user)
        {
            string result = GenerateTasteListString(user, true);
            Log(result);
            user.Player.MsgLocStr(result);
        }

        private static void HandleDietRequest(User user, int meals)
        {
            if (user == null || user.Player == null) return;

            string userId = user.Name;
            DietResult cached = null;

            lock(_lock)
            {
                if (DietCache.ContainsKey(userId))
                {
                    cached = DietCache[userId];
                }
            }

            if (cached != null)
            {
                if ((DateTime.Now - cached.GeneratedAt).TotalMinutes < CooldownMinutes)
                {
                     if (meals > 0)
                         DisplayShoppingList(user, cached.Plan, meals);
                     else
                         DisplayDiet(user, cached.Plan);

                     var remaining = TimeSpan.FromMinutes(CooldownMinutes) - (DateTime.Now - cached.GeneratedAt);
                     if (remaining.TotalMinutes < 1)
                     {
                         user.Player.MsgLocStr($"Diet updated recently. Next update in {remaining.Seconds} seconds.");
                     }
                     else
                     {
                         user.Player.MsgLocStr($"Next diet update available in {remaining.Hours}h {remaining.Minutes}m.");
                     }
                     return;
                }
            }

            if (meals > 0 && cached == null)
            {
                 user.Player.MsgLocStr("No cached diet found. Calculating new one...");
            }
            else if (meals > 0)
            {
                 user.Player.MsgLocStr("Diet cache expired. Recalculating...");
            }
            else
            {
                 // Default case: /diet with no args and no cache
                 user.Player.MsgLocStr("Calculating diet...");
            }

            DietPlan newPlan = FindBestDiet(user);

            if (newPlan != null)
            {
                lock(_lock)
                {
                    DietCache[userId] = new DietResult { GeneratedAt = DateTime.Now, Plan = newPlan };
                    SaveData();
                }

                if (meals > 0)
                    DisplayShoppingList(user, newPlan, meals);
                else
                    DisplayDiet(user, newPlan);
            }
            else
            {
                user.Player.MsgLocStr("Could not find a suitable diet. Try discovering/tasting more foods or toggling Strict Mode (/diet strict)!");
            }
        }

        private static DietPlan FindBestDiet(User user)
        {
            Log($"Starting diet calculation for {user.Name}. StrictMode: {StrictMode}");
            float stomachSize = 3000;
            try {
                var stomachProp = user.GetType().GetProperty("Stomach");
                if (stomachProp != null)
                {
                    var stomach = stomachProp.GetValue(user);
                    if (stomach != null)
                    {
                        var capProp = stomach.GetType().GetProperty("Capacity");
                        if (capProp != null) stomachSize = (float)capProp.GetValue(stomach);
                    }
                }
            } catch { }
            Log($"Stomach size: {stomachSize}");

            var availableFoods = new List<(FoodItem Item, int Tier, int Level, string Preference)>();

            // Get Known Tastes (Reliable Source)
            var tasteData = GetTasteData(user);
            Log($"Taste Data Found: {tasteData.Count} items.");

            // Cold Start Fix: Aggressive Wake-Up
            if (tasteData.Count == 0)
            {
                Log("TasteBuds returned 0 items. Attempting to wake up the system...");
                try {
                     // 1. Stomach Contents
                     var stomach = user.GetType().GetProperty("Stomach")?.GetValue(user);
                     if (stomach != null) {
                         var contents = stomach.GetType().GetProperty("Contents")?.GetValue(stomach);
                         if (contents == null) stomach.GetType().GetField("Contents")?.GetValue(stomach);
                     }

                     // 2. Inventory
                     var inv = user.GetType().GetProperty("Inventory")?.GetValue(user);
                     if (inv != null)
                     {
                         inv.GetType().GetProperty("Toolbar")?.GetValue(inv);
                         inv.GetType().GetProperty("Backpack")?.GetValue(inv);
                     }

                     // 3. Skillset
                     user.GetType().GetProperty("Skillset")?.GetValue(user);

                     // Retry
                     tasteData = GetTasteData(user);
                     Log($"Retry Taste Data Found: {tasteData.Count} items.");
                } catch (Exception ex) { Log($"WakeUp Error: {ex}"); }
            }

            // Determine exclusions based on taste
            var knownBadTypes = new HashSet<Type>();
            var knownGoodTypes = new Dictionary<Type, string>();
            bool favDiscovered = IsFavoriteDiscovered(user);

            foreach(var t in tasteData)
            {
                string p = t.Preference;
                if (p.Equals("Bad", StringComparison.OrdinalIgnoreCase) ||
                    p.Equals("Horrible", StringComparison.OrdinalIgnoreCase) ||
                    p.Equals("Worst", StringComparison.OrdinalIgnoreCase))
                {
                    knownBadTypes.Add(t.Type);
                }
                else if (p.Equals("Favorite", StringComparison.OrdinalIgnoreCase))
                {
                    if (favDiscovered) knownGoodTypes[t.Type] = p;
                    else knownBadTypes.Add(t.Type); // Treat as excluded/hidden
                }
                else if (p.Equals("Delicious", StringComparison.OrdinalIgnoreCase) ||
                         p.Equals("Good", StringComparison.OrdinalIgnoreCase) ||
                         p.Equals("Ok", StringComparison.OrdinalIgnoreCase))
                {
                    knownGoodTypes[t.Type] = p;
                }
            }

            void AddFoodIfValid(FoodItem food)
            {
                if (IsHiddenOrBlacklisted(food)) return;
                if (food.Calories <= 0) return;
                if (food.Calories > stomachSize) return;

                string name = food.DisplayName.ToString();
                if (name.StartsWith("Raw ") || name.Contains(" Yeast") || name.Contains("Flour"))
                {
                     if (IsIngredient(food)) return;
                }

                var (tier, level) = GetFoodRank(food);
                string pref = "Unknown";
                if (knownGoodTypes.ContainsKey(food.Type)) pref = knownGoodTypes[food.Type];

                availableFoods.Add((food, tier, level, pref));
            }

            if (StrictMode)
            {
                foreach(var kvp in knownGoodTypes)
                {
                     try
                     {
                         var item = Item.Get(kvp.Key);
                         if (item is FoodItem food) AddFoodIfValid(food);
                     } catch {}
                }
            }
            else
            {
                IEnumerable<FoodItem> allFoods = null;
                try
                {
                     allFoods = Item.AllItemsIncludingHidden.OfType<FoodItem>();
                }
                catch
                {
                     try {
                         var itemType = typeof(Item);
                         var prop = itemType.GetProperty("AllItems", BindingFlags.Public | BindingFlags.Static);
                         if (prop == null) prop = itemType.GetProperty("AllItemsIncludingHidden", BindingFlags.Public | BindingFlags.Static);

                         if (prop != null)
                         {
                             var items = prop.GetValue(null) as IEnumerable<Item>;
                             if (items != null) allFoods = items.OfType<FoodItem>();
                         }
                     } catch {}
                }

                if (allFoods != null)
                {
                    foreach(var food in allFoods)
                    {
                        if (knownBadTypes.Contains(food.Type)) continue; // Skip known bad
                        AddFoodIfValid(food);
                    }
                }
            }

            Log($"Total available foods for calculation: {availableFoods.Count}");
            if (availableFoods.Count == 0) return null;

            // Strategy: STRICT TIER > TASTE > BALANCE > CALORIES
            // 1. Filter by Max Tier
            int maxTier = availableFoods.Max(x => x.Tier);
            var tierPool = availableFoods.Where(x => x.Tier == maxTier).ToList();

            // Fallback for Variety: If top tier pool is too small (< 2), include next tier down
            if (tierPool.Count < 2)
            {
                tierPool = availableFoods.Where(x => x.Tier >= maxTier - 1).ToList();
            }

            // 2. Separate into Taste Pools
            // High Taste: Favorite (5), Delicious (4), Good (3)
            // Med Taste: Ok (2), Unknown (1)
            var highTastePool = tierPool.Where(x => GetTasteScore(x.Preference) >= 3).ToList();
            var medTastePool = tierPool.Where(x => GetTasteScore(x.Preference) >= 1).ToList(); // Includes High + Med

            int MAX_ITERATIONS = 3000;
            List<DietPlan> candidates = new List<DietPlan>();

            // Phase 1: Try with ONLY High Taste foods
            if (highTastePool.Count > 0)
            {
                for(int i=0; i < MAX_ITERATIONS / 2; i++)
                {
                    var plan = GenerateRandomPlan(highTastePool, stomachSize);
                    if (plan != null) candidates.Add(plan);
                }
            }

            // Phase 2: Try with ALL acceptable foods (High + Med)
            if (medTastePool.Count > 0)
            {
                for(int i=0; i < MAX_ITERATIONS / 2; i++)
                {
                    var plan = GenerateRandomPlan(medTastePool, stomachSize);
                    if (plan != null) candidates.Add(plan);
                }
            }

            // Sort Results:
            // 1. Tier (Desc) - Handled by pool selection mostly, but check avg just in case
            // 2. Avg Taste (Desc) - PRIMARY user request ("Taste > Nutrient Balance")
            // 3. Balance Variance (Asc) - "Balanced Approach"
            // 4. Total Calories (Desc)

            var sorted = candidates
                .OrderByDescending(d => d.AverageTier)
                .ThenByDescending(d => d.AverageTasteScore)
                .ThenBy(d => d.Score)
                .ThenByDescending(d => d.TotalCalories)
                .ToList();

            // Filter out unbalanced diets unless they are the only option
            // (e.g. dont pick a 100% Meat diet (Variance 1800) just because it tastes 5.0 vs 4.8)
            // Heuristic: If we have a 'Good Balance' diet (Variance < 100), prefer it over a 'Bad Balance' one
            // unless the Bad Balance one is significantly tastier?

            var goodBalance = sorted.Where(d => d.Score < 15).ToList(); // Score < 15 (Variance < 225) ensures > 0% nutrients
            if (goodBalance.Count > 0)
            {
                 // If we have balanced options, sort THOSE by Taste first
                 return goodBalance.OrderByDescending(d => d.AverageTasteScore)
                                   .ThenBy(d => d.Score)
                                   .ThenByDescending(d => d.TotalCalories)
                                   .FirstOrDefault();
            }

            return sorted.FirstOrDefault();
        }

        private static int GetTasteScore(string preference)
        {
            if (string.IsNullOrEmpty(preference)) return 1;
            if (preference.Equals("Favorite", StringComparison.OrdinalIgnoreCase)) return 5;
            if (preference.Equals("Delicious", StringComparison.OrdinalIgnoreCase)) return 4;
            if (preference.Equals("Good", StringComparison.OrdinalIgnoreCase)) return 3;
            if (preference.Equals("Ok", StringComparison.OrdinalIgnoreCase)) return 2;
            return 1; // Unknown or other
        }

        private static DietPlan GenerateRandomPlan(List<(FoodItem Item, int Tier, int Level, string Preference)> pool, float stomachSize)
        {
            var diet = new List<(FoodItem Item, int Tier, int Level, string Preference)>();
            float currentCals = 0;

            // Target Variety: Try to pick 3-5 distinct items if possible
            int distinctTarget = Math.Min(pool.Count, 4);
            var shuffled = pool.OrderBy(x => rng.Next()).ToList();

            // Initial Fill (Distinct items for variety)
            for(int k=0; k<distinctTarget; k++)
            {
                if (currentCals + shuffled[k].Item.Calories <= stomachSize)
                {
                    diet.Add(shuffled[k]);
                    currentCals += shuffled[k].Item.Calories;
                }
            }

            // Random Fill for remaining space
            int attemptLimit = 100;
            while(currentCals < stomachSize && attemptLimit > 0)
            {
                var candidate = pool[rng.Next(pool.Count)];
                if (currentCals + candidate.Item.Calories <= stomachSize)
                {
                     diet.Add(candidate);
                     currentCals += candidate.Item.Calories;
                }
                else
                {
                    // Full or doesn't fit
                    attemptLimit--;
                }
            }

            if (currentCals < stomachSize * 0.8) return null; // Too small
            return AnalyzeDiet(diet);
        }

        private static (int Tier, int Level) GetFoodRank(FoodItem food)
        {
            try
            {
                int tier = 0;
                // 1. Try Direct Property "Tier" on Item as fallback/initial
                var tierProp = food.GetType().GetProperty("Tier");
                if (tierProp != null)
                {
                    int val = Convert.ToInt32(tierProp.GetValue(food));
                    if (val > 0) tier = val;
                }

                var skillAttrs = food.GetType().GetCustomAttributes(false);

                foreach(var attr in skillAttrs)
                {
                    string typeName = attr.GetType().Name;
                    if (typeName.Contains("RequiresSkill"))
                    {
                        var skillTypeProp = attr.GetType().GetProperty("SkillItemType") ?? attr.GetType().GetProperty("SkillType");
                        var levelProp = attr.GetType().GetProperty("Level");

                        if (skillTypeProp != null)
                        {
                            var skillType = skillTypeProp.GetValue(attr) as Type;
                            if (skillType != null)
                            {
                                string skillName = skillType.Name;
                                if (skillName.Contains("CuttingEdgeCooking")) tier = 4;
                                else if (skillName.Contains("AdvancedCooking") || skillName.Contains("AdvancedBaking")) tier = 3;
                                else if (skillName.Contains("Cooking") || skillName.Contains("Baking")) tier = 2;
                                else if (skillName.Contains("Campfire")) tier = 1;
                            }
                        }

                        if (levelProp != null && tier > 0)
                        {
                            int level = Convert.ToInt32(levelProp.GetValue(attr));
                            return (tier, level);
                        }
                    }
                }

                return (tier, 0);
            }
            catch {}
            return (0, 0);
        }

        private static DietPlan AnalyzeDiet(List<(FoodItem Item, int Tier, int Level, string Preference)> diet)
        {
            if (diet.Count == 0) return new DietPlan { Score = double.MaxValue };

            float c = 0, f = 0, p = 0, v = 0, cals = 0;
            float totalTier = 0;
            float totalLevel = 0;
            float totalTaste = 0;
            var counts = new Dictionary<string, int>();

            foreach(var entry in diet)
            {
                var item = entry.Item;
                c += item.Nutrition.Carbs;
                f += item.Nutrition.Fat;
                p += item.Nutrition.Protein;
                v += item.Nutrition.Vitamins;
                cals += item.Calories;

                totalTier += entry.Tier;
                totalLevel += entry.Level;
                totalTaste += GetTasteScore(entry.Preference);

                string name = item.UILink(); // Use UILink for interactive chat tags
                if (!counts.ContainsKey(name)) counts[name] = 0;
                counts[name]++;
            }

            float totalNutrients = c + f + p + v;
            double score = double.MaxValue;

            if (totalNutrients > 0)
            {
                double cp = (c / totalNutrients) * 100;
                double fp = (f / totalNutrients) * 100;
                double pp = (p / totalNutrients) * 100;
                double vp = (v / totalNutrients) * 100;

                double variance = (Math.Pow(cp - 25, 2) + Math.Pow(fp - 25, 2) + Math.Pow(pp - 25, 2) + Math.Pow(vp - 25, 2)) / 4;
                score = Math.Sqrt(variance);
            }

            return new DietPlan
            {
                Foods = counts,
                Score = score,
                TotalCalories = cals,
                Carbs = c,
                Fat = f,
                Protein = p,
                Vitamins = v,
                AverageTier = totalTier / diet.Count,
                AverageLevel = totalLevel / diet.Count,
                AverageTasteScore = totalTaste / diet.Count
            };
        }

        private static void DisplayDiet(User user, DietPlan plan)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"<b>Recommended Diet</b> (Score: {plan.Score:F2}, Cals: {plan.TotalCalories:F0}, Tier: {plan.AverageTier:F1}, Taste: {plan.AverageTasteScore:F1})");
            sb.AppendLine("<b>Eat (Per Meal):</b>");
            foreach(var kvp in plan.Foods)
            {
                sb.AppendLine($"- {kvp.Key}: {kvp.Value}");
            }

            float total = plan.Carbs + plan.Fat + plan.Protein + plan.Vitamins;
            if (total > 0)
            {
                float cp = (plan.Carbs / total) * 100;
                float pp = (plan.Protein / total) * 100;
                float fp = (plan.Fat / total) * 100;
                float vp = (plan.Vitamins / total) * 100;

                string cCarbs = "#e64a17";
                string cProt  = "#e69d08";
                string cFat   = "#deb719";
                string cVit   = "#9fc80d";
                string ColorText(string t, string h) => $"<color={h}>{t}</color>";

                sb.AppendLine($"Expected balance: {ColorText("Carbs", cCarbs)}: {cp:F1}%, {ColorText("Protein", cProt)}: {pp:F1}%, {ColorText("Fat", cFat)}: {fp:F1}%, {ColorText("Vitamins", cVit)}: {vp:F1}%");
            }
            else
            {
                sb.AppendLine("Expected balance: Carbs: 0.0%, Protein: 0.0%, Fat: 0.0%, Vitamins: 0.0%");
            }

            Log(sb.ToString());
            user.Player.MsgLocStr(sb.ToString());
        }

        private static void DisplayShoppingList(User user, DietPlan plan, int meals)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"<b>Shopping List for {meals} Meals</b>");
            foreach(var kvp in plan.Foods)
            {
                sb.AppendLine($"- {kvp.Key}: {kvp.Value * meals}");
            }
            Log(sb.ToString());
            user.Player.MsgLocStr(sb.ToString());
        }

        private static bool IsHiddenOrBlacklisted(FoodItem food)
        {
            // Blacklist for Safe Mode (Preventing Spoilers)
            var name = food.DisplayName.ToString().ToLower();
            if (name.Contains("ecoylent") ||
                name.Contains("admin") ||
                name.Contains("dev tool") ||
                name.Contains("creative") ||
                name.Contains("spawn")) return true;

            try
            {
                var prop = food.GetType().GetProperty("Hidden");
                if (prop != null && (bool)prop.GetValue(food)) return true;

                // Check Tags
                var tagsProp = food.GetType().GetProperty("Tags");
                if (tagsProp != null)
                {
                    var tags = tagsProp.GetValue(food) as IEnumerable<string>;
                    if (tags != null)
                    {
                        foreach(var t in tags)
                        {
                            if (t.Equals("Dev", StringComparison.OrdinalIgnoreCase) ||
                                t.Equals("Hidden", StringComparison.OrdinalIgnoreCase) ||
                                t.Equals("Admin", StringComparison.OrdinalIgnoreCase)) return true;
                        }
                    }
                }
            }
            catch {}
            return false;
        }

        private static bool IsIngredient(FoodItem food)
        {
            try
            {
                var tagsProp = food.GetType().GetProperty("Tags");
                if (tagsProp != null)
                {
                    var tags = tagsProp.GetValue(food) as IEnumerable<string>;
                    if (tags != null)
                    {
                        foreach(var t in tags)
                        {
                            if (t.Equals("Ingredient", StringComparison.OrdinalIgnoreCase)) return true;
                        }
                    }
                }
            }
            catch {}
            return false;
        }

        private static void ProbeReflection(User user)
        {
             // Enhanced probe
             var sb = new StringBuilder();
             sb.AppendLine("Reflection Probe V4:");
             try {
                 var tastes = GetTasteData(user);
                 sb.AppendLine($"Found {tastes.Count} taste entries.");

                 // Inspect a sample food item
                 if (tastes.Count > 0)
                 {
                     var type = tastes.First().Type;
                     var item = Item.Get(type);
                     if (item != null)
                     {
                         sb.AppendLine($"Inspecting {item.DisplayName}:");
                         foreach(var prop in item.GetType().GetProperties())
                         {
                             try {
                                 var val = prop.GetValue(item);
                                 sb.AppendLine($"- {prop.Name}: {val}");
                             } catch {}
                         }
                     }
                 }
             } catch (Exception ex) { sb.AppendLine($"Error: {ex.Message}"); }
             Log(sb.ToString());
             user.Player.MsgLocStr("Probe results logged to file (too large for chat).");
        }

        private static bool IsFavoriteDiscovered(User user)
        {
            try
            {
                var stomach = user.GetType().GetProperty("Stomach")?.GetValue(user);
                var tasteBuds = stomach?.GetType().GetProperty("TasteBuds")?.GetValue(stomach);
                if (tasteBuds != null)
                {
                    var favProp = tasteBuds.GetType().GetProperty("FavoriteDiscovered");
                    if (favProp != null) return (bool)favProp.GetValue(tasteBuds);
                }
            } catch {}
            return false;
        }

        private static bool IsWorstDiscovered(User user)
        {
            try
            {
                var stomach = user.GetType().GetProperty("Stomach")?.GetValue(user);
                var tasteBuds = stomach?.GetType().GetProperty("TasteBuds")?.GetValue(stomach);
                if (tasteBuds != null)
                {
                    var worstProp = tasteBuds.GetType().GetProperty("WorstDiscovered");
                    if (worstProp != null) return (bool)worstProp.GetValue(tasteBuds);
                }
            } catch {}
            return false;
        }

        // --- NEW HELPER METHOD ---
        // Returns list of (FoodType, PreferenceString)
        private static List<(Type Type, string Preference)> GetTasteData(User user)
        {
            var results = new List<(Type Type, string Preference)>();
            try
            {
                var stomachProp = user.GetType().GetProperty("Stomach");
                if (stomachProp == null) return results;
                var stomach = stomachProp.GetValue(user);
                if (stomach == null) return results;
                var tasteBudsProp = stomach.GetType().GetProperty("TasteBuds");
                if (tasteBudsProp == null) return results;
                var tasteBuds = tasteBudsProp.GetValue(stomach);
                if (tasteBuds == null) return results;

                var foodToTasteProp = tasteBuds.GetType().GetProperty("FoodToTaste") ?? tasteBuds.GetType().GetField("FoodToTaste") as MemberInfo;
                if (foodToTasteProp == null) return results;

                object foodToTasteDict = null;
                if (foodToTasteProp is PropertyInfo pInfo) foodToTasteDict = pInfo.GetValue(tasteBuds);
                else if (foodToTasteProp is FieldInfo fInfo) foodToTasteDict = fInfo.GetValue(tasteBuds);

                if (foodToTasteDict == null) return results;

                var enumerable = foodToTasteDict as System.Collections.IEnumerable;
                if (enumerable != null)
                {
                     foreach (var entry in enumerable)
                     {
                         try
                         {
                             var entryType = entry.GetType();
                             var keyProp = entryType.GetProperty("Key");
                             var valueProp = entryType.GetProperty("Value");

                             if (keyProp == null || valueProp == null) continue;

                             object keyObj = keyProp.GetValue(entry);
                             object valueObj = valueProp.GetValue(entry);

                             if (keyObj == null || valueObj == null) continue;

                             var type = keyObj as Type;
                             if (type == null) continue;

                             object enumVal = null;
                             var prefProp = valueObj.GetType().GetProperty("Preference");
                             if (prefProp != null) enumVal = prefProp.GetValue(valueObj);
                             else
                             {
                                 var prefField = valueObj.GetType().GetField("Preference");
                                 if (prefField != null) enumVal = prefField.GetValue(valueObj);
                             }

                             if (enumVal != null)
                             {
                                 results.Add((type, enumVal.ToString()));
                             }
                         }
                         catch { }
                     }
                }
            }
            catch (Exception ex) { Log($"GetTasteData Error: {ex}"); }
            return results;
        }

        private static List<Type> GetDiscoveredFoodTypesFromTasteBuds(User user)
        {
             // Refactored to wrapper using GetTasteData
             var list = new List<Type>();
             try {
                 var data = GetTasteData(user);
                 var allowed = new[] { "Favorite", "Delicious", "Good", "Ok" };
                 bool favDisc = IsFavoriteDiscovered(user);

                 foreach(var d in data)
                 {
                     if (d.Preference.Equals("Favorite", StringComparison.OrdinalIgnoreCase) && !favDisc) continue;
                     if (allowed.Any(a => a.Equals(d.Preference, StringComparison.OrdinalIgnoreCase)))
                     {
                         list.Add(d.Type);
                     }
                 }
             } catch {}
             return list;
        }

        private static Dictionary<string, List<string>> GetGroupedFoodTypesFromTasteBuds(User user)
        {
            var grouped = new Dictionary<string, List<string>>();
            var data = GetTasteData(user);

            foreach(var d in data)
            {
                string foodName = "Unknown";
                try {
                     var item = Item.Get(d.Type);
                     if (item != null) foodName = item.UILink();
                } catch {}

                if (!grouped.ContainsKey(d.Preference)) grouped[d.Preference] = new List<string>();
                grouped[d.Preference].Add(foodName);
            }
            return grouped;
        }
    }
}
