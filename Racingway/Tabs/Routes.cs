using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using ImGuiNET;
using LiteDB;
//using Newtonsoft.Json;
using Racingway.Race;
using Racingway.Race.Collision;
using Racingway.Race.Collision.Triggers;
using Racingway.Utils;

namespace Racingway.Tabs
{
    public class Routes : ITab
    {
        public string Name => "Routes";

        private Plugin Plugin { get; }

        private bool hasStart = false;
        private bool hasFinish = false;

        public Routes(Plugin plugin)
        {
            this.Plugin = plugin;
            updateStartFinishBools();
        }

        public void Dispose() { }

        public void Draw()
        {
            ImGui.Text($"Current position: {Plugin.ClientState.LocalPlayer.Position.ToString()}");

            int id = 0;

            using (var tree = ImRaii.TreeNode("Routes"))
            {
                if (tree.Success)
                {
                    foreach (Route route in Plugin.LoadedRoutes)
                    {
                        id++;
                        if (
                            ImGui.Selectable(
                                $"{route.Name}##{id}",
                                route.Id == Plugin.SelectedRoute
                            )
                        )
                        {
                            if (route.Id == Plugin.SelectedRoute)
                                return;
                            Plugin.SelectedRoute = route.Id;
                        }
                        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                        {
                            ImGui.SetTooltip(route.Id.ToString());
                        }
                    }
                }
            }

            Route? selectedRoute = Plugin.LoadedRoutes.FirstOrDefault(
                x => x.Id == Plugin.SelectedRoute,
                new Route(
                    string.Empty,
                    Plugin.CurrentAddress,
                    string.Empty,
                    new List<ITrigger>(),
                    new List<Record>()
                )
            );

            if (ImGuiComponents.IconButton(Dalamud.Interface.FontAwesomeIcon.FileImport))
            {
                string data = ImGui.GetClipboardText();

                Plugin.DataQueue.QueueDataOperation(async () =>
                {
                    await Plugin.Storage.ImportRouteFromBase64(data);
                });
            }
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.SetTooltip("Import config from clipboard.");
            }

            ImGui.SameLine();
            if (ImGuiComponents.IconButton(Dalamud.Interface.FontAwesomeIcon.FileExport))
            {
                string input = System.Text.Json.JsonSerializer.Serialize(
                    selectedRoute.GetEmptySerialized()
                );
                string text = Compression.ToCompressedBase64(input);
                if (text != string.Empty)
                {
                    ImGui.SetClipboardText(text);
                }
                else
                {
                    Plugin.ChatGui.PrintError("[RACE] No route selected.");
                }
            }
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.SetTooltip("Export config to clipboard.");
            }

            ImGui.SameLine();
            if (ImGuiComponents.IconButton(Dalamud.Interface.FontAwesomeIcon.Recycle))
            {
                Plugin.territoryHelper.GetLocationID();
            }

            if (selectedRoute == null)
            {
                ImGui.Text("If you're seeing this text, consider pressing the refresh button.");
                return;
            }

            string name = selectedRoute.Name;
            if (ImGui.InputText("Name", ref name, 64) && name != selectedRoute.Name)
            {
                // Something
                if (name == string.Empty)
                    return;
                selectedRoute.Name = name;
                updateRoute(selectedRoute);
            }

            string description = selectedRoute.Description;
            if (
                ImGui.InputText("Description", ref description, 256)
                && description != selectedRoute.Description
            )
            {
                selectedRoute.Description = description;
                updateRoute(selectedRoute);
            }

            if (ImGui.Button("Add Trigger"))
            {
                // We set the trigger position slightly below the player due to SE position jank.
                Checkpoint newTrigger = new Checkpoint(
                    selectedRoute,
                    Plugin.ClientState.LocalPlayer.Position - new Vector3(0, 0.1f, 0),
                    Vector3.One,
                    Vector3.Zero
                );
                selectedRoute.Triggers.Add(newTrigger);
                updateRoute(selectedRoute);
            }

            for (int i = 0; i < selectedRoute.Triggers.Count; i++)
            {
                ITrigger trigger = selectedRoute.Triggers[i];

                ImGui.Separator();
                if (ImGuiComponents.IconButton(id, Dalamud.Interface.FontAwesomeIcon.Eraser))
                {
                    updateStartFinishBools();
                    selectedRoute.Triggers.Remove(trigger);
                    updateRoute(selectedRoute);
                    continue;
                }
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                {
                    ImGui.SetTooltip("Erase this trigger.");
                }

                id++;
                ImGui.SameLine();
                if (ImGuiComponents.IconButton(id, Dalamud.Interface.FontAwesomeIcon.ArrowsToDot))
                {
                    // We set the trigger position slightly below the player due to SE position jank.
                    selectedRoute.Triggers[i].Cube.Position =
                        Plugin.ClientState.LocalPlayer.Position - new Vector3(0, 0.1f, 0);
                    Plugin.ChatGui.Print($"[RACE] Trigger position set to {trigger.Cube.Position}");

                    updateRoute(selectedRoute);
                }
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                {
                    ImGui.SetTooltip("Set trigger position to your characters position.");
                }

                ImGui.SameLine();
                if (ImGui.TreeNode($"Type##{id}"))
                {
                    ImGui.Indent();

                    if (ImGui.Selectable("Start", trigger is Start))
                    {
                        if (trigger is Start)
                            return;

                        if (hasStart)
                        {
                            Plugin.ChatGui.PrintError(
                                "[RACE] There is already a start trigger in this route."
                            );
                        }
                        else
                        {
                            selectedRoute.Triggers[i] = new Start(trigger.Route, trigger.Cube);
                            updateRoute(selectedRoute);
                        }
                    }

                    if (ImGui.Selectable("Checkpoint", trigger is Checkpoint))
                    {
                        if (trigger is Checkpoint)
                            return;
                        selectedRoute.Triggers[i] = new Checkpoint(trigger.Route, trigger.Cube);
                        updateRoute(selectedRoute);
                    }

                    if (ImGui.Selectable("Fail", trigger is Fail))
                    {
                        if (trigger is Fail)
                            return;
                        selectedRoute.Triggers[i] = new Fail(trigger.Route, trigger.Cube);
                        updateRoute(selectedRoute);
                    }

                    if (ImGui.Selectable("Finish", trigger is Finish))
                    {
                        if (trigger is Finish)
                            return;
                        if (hasFinish)
                        {
                            Plugin.ChatGui.PrintError(
                                "[RACE] There is already a finish trigger in this route."
                            );
                        }
                        else
                        {
                            selectedRoute.Triggers[i] = new Finish(trigger.Route, trigger.Cube);
                            updateRoute(selectedRoute);
                        }
                    }

                    if (ImGui.Selectable("Loop", trigger is Loop))
                    {
                        if (trigger is Loop)
                            return;
                        selectedRoute.Triggers[i] = new Loop(trigger.Route, trigger.Cube);
                        updateRoute(selectedRoute);
                    }

                    ImGui.Unindent();

                    ImGui.TreePop();
                }

                id++;
                Vector3 position = trigger.Cube.Position;
                if (ImGui.DragFloat3($"Position##{id}", ref position, 0.1f))
                {
                    selectedRoute.Triggers[i].Cube.Position = position;
                    updateRoute(selectedRoute);
                }

                id++;
                Vector3 scale = trigger.Cube.Scale;
                if (ImGui.DragFloat3($"Scale##{id}", ref scale, 0.1f))
                {
                    selectedRoute.Triggers[i].Cube.Scale = scale;
                    selectedRoute.Triggers[i].Cube.UpdateVerts();
                    updateRoute(selectedRoute);
                }

                id++;
                Vector3 rotation = trigger.Cube.Rotation;
                if (ImGui.DragFloat3($"Rotation##{id}", ref rotation, 0.1f))
                {
                    selectedRoute.Triggers[i].Cube.Rotation = rotation;
                    updateRoute(selectedRoute);
                }
            }

            ImGui.Spacing();

            // Add database cleanup section
            if (
                selectedRoute != null
                && selectedRoute.Id != ObjectId.Empty
                && ImGui.CollapsingHeader("Database Cleanup Settings")
            )
            {
                ImGui.Indent();

                bool autoCleanupEnabled = selectedRoute.AutoCleanupEnabled;
                if (ImGui.Checkbox("Enable Automatic Cleanup", ref autoCleanupEnabled))
                {
                    selectedRoute.AutoCleanupEnabled = autoCleanupEnabled;
                    updateRoute(selectedRoute);
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(
                        "Automatically clean up old records after each race to maintain database size"
                    );
                }

                if (autoCleanupEnabled)
                {
                    ImGui.Separator();

                    // Number of maximum records to keep
                    int maxRecordsToKeep = selectedRoute.MaxRecordsToKeep;
                    if (ImGui.SliderInt("Maximum Records", ref maxRecordsToKeep, 10, 1000))
                    {
                        selectedRoute.MaxRecordsToKeep = maxRecordsToKeep;
                        updateRoute(selectedRoute);
                    }

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip("Maximum number of records to keep for this route");
                    }

                    // Top N records to always keep
                    int keepTopNRecords = selectedRoute.KeepTopNRecords;
                    if (ImGui.SliderInt("Keep Top Records", ref keepTopNRecords, 1, 100))
                    {
                        selectedRoute.KeepTopNRecords = keepTopNRecords;
                        updateRoute(selectedRoute);
                    }

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(
                            "Number of top records (fastest times) to always keep regardless of age"
                        );
                    }

                    // Keep Personal Bests option (keep at top for importance)
                    bool keepPersonalBests = selectedRoute.KeepPersonalBests;
                    if (ImGui.Checkbox("Keep Personal Best Times", ref keepPersonalBests))
                    {
                        selectedRoute.KeepPersonalBests = keepPersonalBests;
                        updateRoute(selectedRoute);
                    }

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(
                            "Always keep each player's personal best record, even if it would otherwise be removed by other filters"
                        );
                    }

                    ImGui.Separator();
                    ImGui.Text("Cleanup Filters:");

                    // Delete old records option
                    bool deleteOldRecordsEnabled = selectedRoute.DeleteOldRecordsEnabled;
                    if (ImGui.Checkbox("Delete Records Older Than", ref deleteOldRecordsEnabled))
                    {
                        selectedRoute.DeleteOldRecordsEnabled = deleteOldRecordsEnabled;
                        updateRoute(selectedRoute);
                    }

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(
                            "Remove records that are older than the specified number of days"
                        );
                    }

                    if (deleteOldRecordsEnabled)
                    {
                        ImGui.SameLine();
                        int maxDaysToKeep = selectedRoute.MaxDaysToKeep;
                        if (ImGui.SliderInt("##DaysToKeep", ref maxDaysToKeep, 1, 365, "%d days"))
                        {
                            selectedRoute.MaxDaysToKeep = maxDaysToKeep;
                            updateRoute(selectedRoute);
                        }
                    }

                    // Time threshold filtering
                    bool filterByTimeEnabled = selectedRoute.FilterByTimeEnabled;
                    if (ImGui.Checkbox("Filter By Time Range", ref filterByTimeEnabled))
                    {
                        selectedRoute.FilterByTimeEnabled = filterByTimeEnabled;
                        updateRoute(selectedRoute);
                    }

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(
                            "Filter out records based on completion time (useful for removing junk/abandoned or unusually long runs)"
                        );
                    }

                    if (filterByTimeEnabled)
                    {
                        ImGui.Indent();

                        // Minimum time threshold
                        float minTimeThreshold = selectedRoute.MinTimeThreshold;
                        if (
                            ImGui.SliderFloat(
                                "Minimum Time",
                                ref minTimeThreshold,
                                0.0f,
                                60.0f,
                                "%.1f seconds"
                            )
                        )
                        {
                            selectedRoute.MinTimeThreshold = minTimeThreshold;
                            updateRoute(selectedRoute);
                        }

                        if (ImGui.IsItemHovered())
                        {
                            ImGui.SetTooltip(
                                "Records with time less than this will be removed (good for filtering out abandoned runs)"
                            );
                        }

                        // Maximum time threshold
                        float maxTimeThreshold = selectedRoute.MaxTimeThreshold;
                        if (
                            ImGui.SliderFloat(
                                "Maximum Time",
                                ref maxTimeThreshold,
                                0.0f,
                                3600.0f,
                                maxTimeThreshold > 0 ? "%.1f seconds" : "No Limit"
                            )
                        )
                        {
                            selectedRoute.MaxTimeThreshold = maxTimeThreshold;
                            updateRoute(selectedRoute);
                        }

                        if (ImGui.IsItemHovered())
                        {
                            ImGui.SetTooltip(
                                "Records with time greater than this will be removed (set to 0 for no upper limit)"
                            );
                        }

                        ImGui.Unindent();
                    }

                    ImGui.Separator();

                    // Run cleanup manually
                    if (ImGui.Button("Run Cleanup Now"))
                    {
                        int removed = selectedRoute.ApplyCleanupRules();
                        updateRoute(selectedRoute);
                        Plugin.ChatGui.Print(
                            $"[RACE] Removed {removed} records from '{selectedRoute.Name}'"
                        );
                    }

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip("Manually clean up records based on above settings");
                    }

                    // Display current record count and stats
                    int recordCount = selectedRoute.Records?.Count ?? 0;
                    ImGui.Text($"Current Record Count: {recordCount}");

                    // Show database stats
                    string dbSize = Plugin.Storage.GetFileSizeString();
                    ImGui.Text($"Database Size: {dbSize}");
                }

                ImGui.Unindent();
            }
        }

        private void updateRoute(Route route)
        {
            if (route == null)
                return;

            int index = Plugin.LoadedRoutes.FindIndex(x => x.Id == Plugin.SelectedRoute);
            if (index == -1)
            {
                index = Plugin.LoadedRoutes.FindIndex(x => x == route);
            }

            if (index != -1)
            {
                Plugin.LoadedRoutes[index] = route;
            }
            else
            {
                Plugin.LoadedRoutes.Add(route);
            }

            Plugin.SelectedRoute = route.Id;

            Plugin.SubscribeToRouteEvents();
            updateStartFinishBools();

            Plugin.DataQueue.QueueDataOperation(async () =>
            {
                await Plugin.Storage.AddRoute(route);
                Plugin.Storage.UpdateRouteCache();
            });
        }

        private void updateStartFinishBools()
        {
            try
            {
                if (Plugin.LoadedRoutes.Count == 0)
                    return;

                Route selectedRoute = Plugin.LoadedRoutes.First(x => x.Id == Plugin.SelectedRoute);

                hasStart = false;
                hasFinish = false;

                foreach (ITrigger trigger in selectedRoute.Triggers)
                {
                    if (trigger is Start)
                    {
                        hasStart = true;
                    }
                    if (trigger is Finish)
                    {
                        hasFinish = true;
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.Error(e.ToString());
            }
        }
    }
}
