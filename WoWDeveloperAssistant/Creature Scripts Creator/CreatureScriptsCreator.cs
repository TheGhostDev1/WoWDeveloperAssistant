﻿using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using WoWDeveloperAssistant.Misc;
using static WoWDeveloperAssistant.Packets;
using static WoWDeveloperAssistant.Misc.Utils;

namespace WoWDeveloperAssistant
{
    public class CreatureScriptsCreator
    {
        private MainForm mainForm;
        public static Dictionary<string, Creature> creaturesDict = new Dictionary<string, Creature>();
        public static Dictionary<uint, List<CreatureText>> creatureTextsDict = new Dictionary<uint, List<CreatureText>>();
        public static BuildVersions buildVersion = BuildVersions.BUILD_UNKNOWN;

        public CreatureScriptsCreator(MainForm mainForm)
        {
            this.mainForm = mainForm;
        }

        public void FillSpellsGrid()
        {
            if (mainForm.listBox_CreatureGuids.SelectedItem == null)
                return;

            Creature creature = creaturesDict[mainForm.listBox_CreatureGuids.SelectedItem.ToString()];
            List<Spell> spellsList = new List<Spell>(from spell in creature.castedSpells.Values orderby spell.spellStartCastTimes.Count != 0 ? spell.spellStartCastTimes.Min() : new TimeSpan() ascending select spell);

            mainForm.dataGridView_Spells.Rows.Clear();

            if (mainForm.checkBox_OnlyCombatSpells.Checked)
            {
                foreach (Spell spell in spellsList)
                {
                    if (spell.isCombatSpell)
                    {
                        mainForm.dataGridView_Spells.Rows.Add(spell.spellId, spell.name, spell.spellStartCastTimes.Min().ToFormattedString(), spell.combatCastTimings.minCastTime.ToFormattedString(), spell.combatCastTimings.maxCastTime.ToFormattedString(), spell.combatCastTimings.minRepeatTime.ToFormattedString(), spell.combatCastTimings.maxRepeatTime.ToFormattedString(), spell.castTimes, spell);
                    }
                }
            }
            else
            {
                foreach (Spell spell in spellsList)
                {
                    mainForm.dataGridView_Spells.Rows.Add(spell.spellId, spell.name, spell.combatCastTimings.minCastTime.ToFormattedString(), 0, 0, 0, 0, spell.castTimes, spell);
                }
            }

            mainForm.dataGridView_Spells.Enabled = true;
        }

        public void FillListBoxWithGuids()
        {
            mainForm.listBox_CreatureGuids.Items.Clear();
            mainForm.dataGridView_Spells.Rows.Clear();

            foreach (Creature creature in creaturesDict.Values)
            {
                if (mainForm.checkBox_OnlyCombatSpells.Checked && !creature.HasCombatSpells())
                    continue;

                if (creature.castedSpells.Count == 0)
                    continue;

                if (mainForm.toolStripTextBox_CSC_CreatureEntry.Text != "" && mainForm.toolStripTextBox_CSC_CreatureEntry.Text != "0")
                {
                    if (mainForm.toolStripTextBox_CSC_CreatureEntry.Text == creature.entry.ToString() ||
                        mainForm.toolStripTextBox_CSC_CreatureEntry.Text == creature.guid)
                    {
                        mainForm.listBox_CreatureGuids.Items.Add(creature.guid);
                    }
                }
                else
                {
                    mainForm.listBox_CreatureGuids.Items.Add(creature.guid);
                }
            }

            mainForm.listBox_CreatureGuids.Refresh();
            mainForm.listBox_CreatureGuids.Enabled = true;
        }

        public bool GetDataFromSniffFile(string fileName)
        {
            mainForm.SetCurrentStatus("Loading DBC...");

            DBC.Load();

            mainForm.SetCurrentStatus("Getting lines...");

            var lines = File.ReadAllLines(fileName);
            Dictionary<long, Packet.PacketTypes> packetIndexes = new Dictionary<long, Packet.PacketTypes>();

            buildVersion = LineGetters.GetBuildVersion(lines);
            if (buildVersion == BuildVersions.BUILD_UNKNOWN)
            {
                MessageBox.Show(fileName + " has non-supported build.", "File Error", MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
                return false;
            }

            creaturesDict.Clear();

            mainForm.SetCurrentStatus("Searching for packet indexes in lines...");

            Parallel.For(0, lines.Length, index =>
            {
                if (lines[index].Contains("SMSG_UPDATE_OBJECT") &&
                !packetIndexes.ContainsKey(index))
                {
                    lock (packetIndexes)
                        packetIndexes.Add(index, Packet.PacketTypes.SMSG_UPDATE_OBJECT);
                }
                else if (lines[index].Contains("SMSG_AI_REACTION") &&
                !packetIndexes.ContainsKey(index))
                {
                    lock (packetIndexes)
                        packetIndexes.Add(index, Packet.PacketTypes.SMSG_AI_REACTION);
                }
                else if (lines[index].Contains("SMSG_SPELL_START") &&
                !packetIndexes.ContainsKey(index))
                {
                    lock (packetIndexes)
                        packetIndexes.Add(index, Packet.PacketTypes.SMSG_SPELL_START);
                }
                else if (lines[index].Contains("SMSG_CHAT") &&
                !packetIndexes.ContainsKey(index))
                {
                    lock (packetIndexes)
                        packetIndexes.Add(index, Packet.PacketTypes.SMSG_CHAT);
                }
                else if (lines[index].Contains("SMSG_ON_MONSTER_MOVE") &&
                !packetIndexes.ContainsKey(index))
                {
                    lock (packetIndexes)
                        packetIndexes.Add(index, Packet.PacketTypes.SMSG_ON_MONSTER_MOVE);
                }
                else if (lines[index].Contains("SMSG_ATTACK_STOP") &&
                !packetIndexes.ContainsKey(index))
                {
                    lock (packetIndexes)
                        packetIndexes.Add(index, Packet.PacketTypes.SMSG_ATTACK_STOP);
                }
            });

            mainForm.SetCurrentStatus("Parsing SMSG_UPDATE_OBJECT packets...");

            Parallel.ForEach(packetIndexes.AsEnumerable(), value =>
            {
                if (value.Value == Packet.PacketTypes.SMSG_UPDATE_OBJECT)
                {
                    Parallel.ForEach(UpdateObjectPacket.ParseObjectUpdatePacket(lines, value.Key, buildVersion).AsEnumerable(), packet =>
                    {
                        lock (creaturesDict)
                        {
                            if (!creaturesDict.ContainsKey(packet.creatureGuid))
                            {
                                creaturesDict.Add(packet.creatureGuid, new Creature(packet));
                            }
                            else
                            {
                                creaturesDict[packet.creatureGuid].UpdateCreature(packet);
                            }
                        }
                    });
                }
            });

            mainForm.SetCurrentStatus("Parsing SMSG_SPELL_START packets...");

            Parallel.ForEach(packetIndexes.AsEnumerable(), value =>
            {
                if (value.Value == Packet.PacketTypes.SMSG_SPELL_START)
                {
                    SpellStartPacket spellPacket = SpellStartPacket.ParseSpellStartPacket(lines, value.Key, buildVersion);
                    if (spellPacket.spellId == 0)
                        return;

                    lock (creaturesDict)
                    {
                        if (creaturesDict.ContainsKey(spellPacket.casterGuid))
                        {
                            if (!creaturesDict[spellPacket.casterGuid].castedSpells.ContainsKey(spellPacket.spellId))
                                creaturesDict[spellPacket.casterGuid].castedSpells.Add(spellPacket.spellId, new Spell(spellPacket));
                            else
                                creaturesDict[spellPacket.casterGuid].UpdateSpells(spellPacket);
                        }
                    }
                }
            });

            mainForm.SetCurrentStatus("Parsing SMSG_AI_REACTION packets...");

            Parallel.ForEach(packetIndexes.AsEnumerable(), value =>
            {
                if (value.Value == Packet.PacketTypes.SMSG_AI_REACTION)
                {
                    AIReactionPacket reactionPacket = AIReactionPacket.ParseAIReactionPacket(lines, value.Key, buildVersion);
                    if (reactionPacket.creatureGuid == "")
                        return;

                    lock (creaturesDict)
                    {
                        if (creaturesDict.ContainsKey(reactionPacket.creatureGuid))
                        {
                            if (creaturesDict[reactionPacket.creatureGuid].combatStartTime == TimeSpan.Zero ||
                                creaturesDict[reactionPacket.creatureGuid].combatStartTime < reactionPacket.packetSendTime)
                            {
                                creaturesDict[reactionPacket.creatureGuid].combatStartTime = reactionPacket.packetSendTime;
                            }

                            creaturesDict[reactionPacket.creatureGuid].UpdateCombatSpells(reactionPacket);
                        }
                    }
                }
            });

            mainForm.SetCurrentStatus("Parsing SMSG_CHAT packets...");

            Parallel.ForEach(packetIndexes.AsEnumerable(), value =>
            {
                if (value.Value == Packet.PacketTypes.SMSG_CHAT)
                {
                    ChatPacket chatPacket = ChatPacket.ParseChatPacket(lines, value.Key, buildVersion);
                    if (chatPacket.creatureGuid == "")
                        return;

                    lock (creaturesDict)
                    {
                        Parallel.ForEach(creaturesDict, creature =>
                        {
                            if (creature.Value.entry == chatPacket.creatureEntry)
                            {
                                if (Math.Floor(creature.Value.combatStartTime.TotalSeconds) == Math.Floor(chatPacket.packetSendTime.TotalSeconds) ||
                                Math.Floor(creature.Value.combatStartTime.TotalSeconds) == Math.Floor(chatPacket.packetSendTime.TotalSeconds) + 1 ||
                                Math.Floor(creature.Value.combatStartTime.TotalSeconds) == Math.Floor(chatPacket.packetSendTime.TotalSeconds) - 1)
                                {
                                    if (creatureTextsDict.ContainsKey(chatPacket.creatureEntry))
                                    {
                                        if (!IsCreatureHasAggroText(chatPacket.creatureEntry))
                                        {
                                            lock (creatureTextsDict)
                                            {
                                                creatureTextsDict[chatPacket.creatureEntry].Add(new CreatureText(chatPacket, true));
                                            }
                                        }
                                    }
                                    else
                                    {
                                        lock (creatureTextsDict)
                                        {
                                            creatureTextsDict.Add(chatPacket.creatureEntry, new List<CreatureText>());
                                            creatureTextsDict[chatPacket.creatureEntry].Add(new CreatureText(chatPacket, true));
                                        }
                                    }
                                }

                                if (Math.Floor(creature.Value.deathTime.TotalSeconds) == Math.Floor(chatPacket.packetSendTime.TotalSeconds) ||
                                Math.Floor(creature.Value.deathTime.TotalSeconds) == Math.Floor(chatPacket.packetSendTime.TotalSeconds) + 1 ||
                                Math.Floor(creature.Value.deathTime.TotalSeconds) == Math.Floor(chatPacket.packetSendTime.TotalSeconds) - 1)
                                {
                                    if (creatureTextsDict.ContainsKey(chatPacket.creatureEntry))
                                    {
                                        if (!IsCreatureHasDeathText(chatPacket.creatureEntry))
                                        {
                                            lock (creatureTextsDict)
                                            {
                                                creatureTextsDict[chatPacket.creatureEntry].Add(new CreatureText(chatPacket, false, true));
                                            }
                                        }
                                    }
                                    else
                                    {
                                        lock (creatureTextsDict)
                                        {
                                            creatureTextsDict.Add(chatPacket.creatureEntry, new List<CreatureText>());
                                            creatureTextsDict[chatPacket.creatureEntry].Add(new CreatureText(chatPacket, false, true));
                                        }
                                    }
                                }
                            }
                        });
                    }
                }
            });

            mainForm.SetCurrentStatus("Parsing SMSG_ON_MONSTER_MOVE and SMSG_ATTACK_STOP packets...");

            Parallel.ForEach(packetIndexes.AsEnumerable(), value =>
            {
                if (value.Value == Packet.PacketTypes.SMSG_ON_MONSTER_MOVE)
                {
                    MonsterMovePacket movePacket = MonsterMovePacket.ParseMovementPacket(lines, value.Key, buildVersion);
                    if (movePacket.creatureGuid == "")
                        return;

                    lock (creaturesDict)
                    {
                        if (creaturesDict.ContainsKey(movePacket.creatureGuid))
                        {
                            creaturesDict[movePacket.creatureGuid].UpdateSpellsByMovementPacket(movePacket);
                        }
                    }
                }
                else if (value.Value == Packet.PacketTypes.SMSG_ATTACK_STOP)
                {
                    AttackStopPacket attackStopPacket = AttackStopPacket.ParseAttackStopkPacket(lines, value.Key, buildVersion);
                    if (attackStopPacket.creatureGuid == "")
                        return;

                    lock (creaturesDict)
                    {
                        if (creaturesDict.ContainsKey(attackStopPacket.creatureGuid))
                        {
                            creaturesDict[attackStopPacket.creatureGuid].UpdateSpellsByAttackStopPacket(attackStopPacket);

                            if (attackStopPacket.nowDead && creaturesDict[attackStopPacket.creatureGuid].deathTime == TimeSpan.Zero)
                            {
                                creaturesDict[attackStopPacket.creatureGuid].deathTime = attackStopPacket.packetSendTime;
                            }
                        }
                    }
                }
            });

            Parallel.ForEach(creaturesDict, creature =>
            {
                creature.Value.RemoveNonCombatCastTimes();
            });

            Parallel.ForEach(creaturesDict, creature =>
            {
                creature.Value.CreateCombatCastTimings();
            });

            Parallel.ForEach(creaturesDict, creature =>
            {
                creature.Value.CreateDeathSpells();
            });

            mainForm.SetCurrentStatus("");
            return true;
        }

        public void FillSQLOutput()
        {
            string SQLtext = "";
            Creature creature = creaturesDict[mainForm.listBox_CreatureGuids.SelectedItem.ToString()];
            int i = 0;

            SQLtext = "UPDATE `creature_template` SET `AIName` = 'SmartAI' WHERE `entry` = " + creature.entry + ";\r\n";
            SQLtext = SQLtext + "DELETE FROM `smart_scripts` WHERE `entryorguid` = " + creature.entry + ";\r\n";
            SQLtext = SQLtext + "INSERT INTO `smart_scripts` (`entryorguid`, `source_type`, `id`, `link`, `event_type`, `event_phase_mask`, `event_chance`, `event_flags`, `event_difficulties`, `event_param1`, `event_param2`, `event_param3`, `event_param4`, `action_type`, `action_param1`, `action_param2`, `action_param3`, `action_param4`, `action_param5`, `action_param6`, `target_type`, `target_param1`, `target_param2`, `target_param3`, `target_x`, `target_y`, `target_z`, `target_o`, `comment`) VALUES\r\n";

            if (IsCreatureHasAggroText(creature.entry))
            {
                SQLtext = SQLtext + "(" + creature.entry + ", 0, " + i + ", 0, 4, 0, 100, 0, '', 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, '" + creature.name + " - On aggro - Say line 0'),\r\n";
                i++;
            }

            if (IsCreatureHasDeathText(creature.entry))
            {
                SQLtext = SQLtext + "(" + creature.entry + ", 0, " + i + ", 0, 6, 0, 100, 0, '', 0, 0, 0, 0, 1, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, '" + creature.name + " - On death - Say line 1'),\r\n";
                i++;
            }

            for (int l = 0; l < mainForm.dataGridView_Spells.RowCount; l++, i++)
            {
                Spell spell = (Spell) mainForm.dataGridView_Spells[8, l].Value;

                if (spell.isDeathSpell)
                {
                    SQLtext = SQLtext + "(" + creature.entry + ", 0, " + i + ", 0, 6, 0, 100, 0, '', 0, 0, 0, 0, 11, " + spell.spellId + ", 0, 0, 0, 0, 0, " + spell.GetTargetType() + ", 0, 0, 0, 0, 0, 0, 0, '" + creature.name + " - On death - Cast " + spell.name + "')";
                }
                else
                {
                    SQLtext = SQLtext + "(" + creature.entry + ", 0, " + i + ", 0, 0, 0, 100, 0, '', " + Math.Floor(spell.combatCastTimings.minCastTime.TotalSeconds) * 1000 + ", " + Math.Floor(spell.combatCastTimings.maxCastTime.TotalSeconds) * 1000 + ", " + Math.Floor(spell.combatCastTimings.minRepeatTime.TotalSeconds) * 1000 + ", " + Math.Floor(spell.combatCastTimings.maxRepeatTime.TotalSeconds) * 1000 + ", 11, " + spell.spellId + ", 0, " + (spell.needConeDelay ? (Math.Floor(spell.spellCastTime.TotalSeconds) + 1) * 1000 : 0) + ", 0, 0, 0, " + spell.GetTargetType() + ", 0, 0, 0, 0, 0, 0, 0, '" + creature.name + " - IC - Cast " + spell.name + "')";
                }

                if (l < mainForm.dataGridView_Spells.RowCount - 1)
                {
                    SQLtext = SQLtext + ",\r\n";
                }
                else
                {
                    SQLtext = SQLtext + ";\r\n";
                }
            }

            mainForm.textBox_SQLOutput.Text = SQLtext;
        }

        public static uint GetCreatureEntryByGuid(string creatureGuid)
        {
            if (creaturesDict.ContainsKey(creatureGuid))
                return creaturesDict[creatureGuid].entry;

            return 0;
        }

        public static bool IsCreatureHasAggroText(uint creatureEntry)
        {
            if (creatureTextsDict.ContainsKey(creatureEntry))
            {
                foreach (CreatureText text in creatureTextsDict[creatureEntry])
                {
                    if (text.isAggroText)
                        return true;
                }
            }

            return false;
        }

        public static bool IsCreatureHasDeathText(uint creatureEntry)
        {
            if (creatureTextsDict.ContainsKey(creatureEntry))
            {
                foreach (CreatureText text in creatureTextsDict[creatureEntry])
                {
                    if (text.isDeathText)
                        return true;
                }
            }

            return false;
        }

        public void OpenFileDialog()
        {
            mainForm.openFileDialog.Title = "Open File";
            mainForm.openFileDialog.Filter = "Parsed Sniff File (*.txt)|*.txt";
            mainForm.openFileDialog.FileName = "*.txt";
            mainForm.openFileDialog.FilterIndex = 1;
            mainForm.openFileDialog.ShowReadOnly = false;
            mainForm.openFileDialog.Multiselect = false;
            mainForm.openFileDialog.CheckFileExists = true;
        }

        public void ImportStarted()
        {
            mainForm.Cursor = Cursors.WaitCursor;
            mainForm.toolStripButton_CSC_ImportSniff.Enabled = false;
            mainForm.toolStripButton_CSC_Search.Enabled = false;
            mainForm.toolStripTextBox_CSC_CreatureEntry.Enabled = false;
            mainForm.listBox_CreatureGuids.Enabled = false;
            mainForm.listBox_CreatureGuids.Items.Clear();
            mainForm.listBox_CreatureGuids.DataSource = null;
            mainForm.dataGridView_Spells.Enabled = false;
            mainForm.dataGridView_Spells.Rows.Clear();
            mainForm.toolStripStatusLabel_FileStatus.Text = "Loading File...";
        }

        public void ImportSuccessful()
        {
            mainForm.toolStripStatusLabel_CurrentAction.Text = "";
            mainForm.toolStripButton_CSC_ImportSniff.Enabled = true;
            mainForm.toolStripButton_CSC_Search.Enabled = true;
            mainForm.toolStripTextBox_CSC_CreatureEntry.Enabled = true;
            mainForm.toolStripStatusLabel_FileStatus.Text = mainForm.openFileDialog.FileName + " is selected for input.";
            mainForm.Cursor = Cursors.Default;
        }
    }
}
