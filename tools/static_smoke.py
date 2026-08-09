from pathlib import Path
import re, sys
ROOT = Path(__file__).resolve().parents[1]
errors=[]; passed=[]
def check(name, cond):
    (passed if cond else errors).append(name)

def text(rel): return (ROOT/rel).read_text(encoding='utf-8')

all_cs=list(ROOT.rglob('*.cs'))
check('C# files present', bool(all_cs))
check('all C# brace counts balanced (coarse)', all(s.read_text(encoding='utf-8').count('{') == s.read_text(encoding='utf-8').count('}') for s in all_cs))
sort=text('HealthAutoArrange.Core/SortPlan.cs')
check('stable tie-break uses original StateIndex', 'a.StateIndex.CompareTo(b.StateIndex)' in sort)
parser=text('HealthAutoArrange.Core/ConfigTextParser.cs')
check('unknown default is Keep', 'var policy = UnknownStatePolicy.Keep;' in parser)
plugin=text('HealthAutoArrange.Plugin/Plugin.cs')
check('refresh hook has Update/AddAll fallback', '"UpdateMoodles"' in plugin and '"AddAllMoodles"' in plugin and '??' in plugin)
check('state catalog uses observed UI nodes', 'Adapter?.RefreshObservedStates()' in plugin)
adapter=text('HealthAutoArrange.Plugin/UnityUiAdapter.cs')
check('adapter records real-node observations', '_observations.Observe(' in adapter and 'RefreshObservedStates' in adapter)
window=text('HealthAutoArrange.Plugin/FallbackSettingsWindow.cs')
check('GUI uses hover info content', 'new GUIContent("i", help' in window and 'GUI.tooltip' in window)
check('advanced/reminders are collapsed sections', '_showAdvanced' in window and '_showReminders' in window)
check('GUI visibly tracks unsaved reminder edits', 'ReminderFingerprint' in window and '_text.Unsaved' in window)
check('state selection no longer autosaves to disk', 'private void SaveSelection' not in window)
editor=text('HealthAutoArrange.Core/GroupSelectionEditor.cs')
check('empty groups are preserved for first assignment', 'EnsureGroup' in editor)
rules=text('HealthAutoArrange.Core/RulesFileStore.cs')
check('rules writer does not delete destination before replacement', 'File.Delete(path)' not in rules and 'File.Replace' in rules)
csproj=text('HealthAutoArrange.Plugin/HealthAutoArrange.Plugin.csproj')
check('game path is configurable', 'HEALTHAUTOARRANGE_GAME_DIR' in csproj and 'HealthAutoArrange.Local.props' in csproj)
check('no fake medical starter mapping', 'Group.Vital.States' not in plugin and 'Reminder.Bleeding' not in plugin)

identity=text('HealthAutoArrange.Core/MoodleIdentity.cs')
selection=text('HealthAutoArrange.Core/SelectionResultGenerator.cs')
check('generated wildcard preserves semantic trailing digits', 'PatternBaseId' in identity and 'NormalizeRuntimeId(state)' in selection)
check('observed catalog is bounded', 'MaxStates = 256' in text('HealthAutoArrange.Core/StateObservationRegistry.cs'))

capture=text('HealthAutoArrange.Core/MoodleCapture.cs')
check('capture resolution can be scoped to current manager', 'ReferenceEquals(item.Manager, manager)' in capture)
check('anchored auto mode requires horizontal row slots', 'hasDistinctHorizontalSlots' in adapter and 'OriginalAnchoredPosition.x' in adapter)

print('HealthAutoArrange static smoke')
for p in passed: print('[PASS]', p)
for e in errors: print('[FAIL]', e)
print(f'Passed {len(passed)}/{len(passed)+len(errors)}')
sys.exit(1 if errors else 0)
