import io, os, re

ROOT = r'F:\development\steam\emulator_bot\streamdeck-elite'
PI = os.path.join(ROOT, 'Elite', 'PropertyInspector', 'Elite')

# action key -> property inspector file, and the id of the <select> holding the function
SOURCES = {
    'repeatingstatic': 'RepeatingStatic.html',
    'uinavigation': 'UiNavigation.html',
    'static': 'Static.html',
    'toggle': 'Toggle.html',
}

OPT = re.compile(r'<option\s+value="([^"]*)"\s*>(.*?)</option>', re.S | re.I)


def esc(s):
    return s.replace('\\', '\\\\').replace('"', '\\"')


blocks = []
for key, fname in SOURCES.items():
    path = os.path.join(PI, fname)
    html = io.open(path, encoding='utf-8-sig', errors='replace').read()

    seen = {}
    for value, label in OPT.findall(html):
        value = value.strip()
        label = re.sub(r'\s+', ' ', re.sub(r'<[^>]+>', '', label)).strip()
        # skip the placeholder rows and anything unlabelled
        if not value or not label or label.startswith('—') or label.startswith('-'):
            continue
        seen.setdefault(value, label)

    entries = '\n'.join(
        f'                    ["{esc(v)}"] = "{esc(l)}",' for v, l in seen.items())
    blocks.append(f'''            ["{key}"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {{
{entries}
                }},''')
    print(f'{key:18s} {len(seen)} labels from {fname}')

body = '\n'.join(blocks)

src = f'''using System;
using System.Collections.Generic;

// ReSharper disable StringLiteralTypo

namespace Elite
{{
    /// <summary>
    /// Friendly names for the function each button can be set to, used to fill in the key title
    /// automatically when a function is picked.
    ///
    /// GENERATED from the property inspector &lt;option&gt; labels by
    /// tools/gen-function-titles - do not edit by hand. Regenerate after changing a PI dropdown
    /// so the titles keep matching what the user actually selected.
    /// </summary>
    public static class FunctionTitles
    {{
        private static readonly Dictionary<string, Dictionary<string, string>> ByAction =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {{
{body}
            }};

        /// <summary>
        /// Title for the given action and function, or empty when there is nothing sensible to
        /// show. The result is wrapped one word per line - the host does not wrap for us, and a
        /// label like "Dec Sensor Range" on one line runs well past the edge of the artwork.
        /// </summary>
        public static string For(string actionKey, string function)
        {{
            if (string.IsNullOrEmpty(actionKey) || string.IsNullOrEmpty(function)) return string.Empty;

            return ByAction.TryGetValue(actionKey, out var map) && map.TryGetValue(function, out var title)
                ? Wrap(title)
                : string.Empty;
        }}

        /// <summary>
        /// Fire groups are picked by index rather than by name, so they get their own rule. The
        /// label stays on one line and the group letter drops to the next, which reads better on
        /// a key than splitting "Fire" and "Group".
        /// </summary>
        public static string ForFireGroup(string firegroup)
        {{
            if (!int.TryParse(firegroup, out var index) || index < 0 || index > 7) return string.Empty;

            return "Fire Group\\n" + (char)('A' + index);
        }}

        /// <summary>
        /// One word per line. Single words are returned unchanged.
        /// </summary>
        private static string Wrap(string title)
        {{
            if (string.IsNullOrWhiteSpace(title)) return string.Empty;

            var words = title.Split(new[] {{ ' ', '\\t' }}, StringSplitOptions.RemoveEmptyEntries);

            return words.Length < 2 ? title : string.Join("\\n", words);
        }}
    }}
}}
'''

out = os.path.join(ROOT, 'Elite', 'FunctionTitles.cs')
io.open(out, 'w', encoding='utf-8-sig', newline='\r\n').write(src)
print('written:', out)
