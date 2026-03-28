import os, re, json
from collections import defaultdict

assets_dir = "./Assets/"
method_r = re.compile(r'(?:public|private|protected|internal)?\s*(?:static\s+)?(?:override\s+|virtual\s+|async\s+)?\s*[\w\<\>\[\]]+\s+(\w+)\s*\([^)]*\)\s*\{?')

logs_by_script = defaultdict(list)
logs_by_tag = defaultdict(list)
logs_by_cat = defaultdict(list)

for root, _, files in os.walk(assets_dir):
    for f in files:
        if not f.endswith('.cs'): continue
        with open(os.path.join(root, f), 'r', encoding='utf-8', errors='ignore') as fp:
            lines = fp.readlines()
        
        cur_method = "Unknown"
        for i, line in enumerate(lines):
            line_str = line.strip()
            if line_str.startswith('//'): continue
            
            m_match = method_r.search(line)
            if m_match and '=' not in line and '==' not in line:
                cur_method = m_match.group(1)
                
            if 'Debug.Log' in line:
                tag_match = re.search(r'\[(.*?)\]', line)
                tag = f"[{tag_match.group(1)}]" if tag_match else "[NoTag]"
                
                cat = "one_shot"
                if cur_method in ['Update', 'LateUpdate', 'FixedUpdate', 'OnGUI', 'OnValidate']:
                    cat = "per_frame"
                else:
                    start_idx = max(0, i - 10)
                    for j in range(start_idx, i):
                        if re.search(r'\b(for|foreach|while)\b', lines[j]) and not lines[j].strip().startswith('//'):
                            cat = "loop"
                            break
                            
                entry = {'file': f, 'method': cur_method, 'tag': tag}
                logs_by_script[f].append(entry)
                logs_by_tag[tag].append(entry)
                logs_by_cat[cat].append(entry)

print("--- FILE SUMMARY ---")
for f, items in sorted(logs_by_script.items(), key=lambda x: len(x[1]), reverse=True)[:10]:
    print(f"{f}: {len(items)} logs")

print("\n--- TAG SUMMARY ---")
for t, items in sorted(logs_by_tag.items(), key=lambda x: len(x[1]), reverse=True)[:15]:
    print(f"{t}: {len(items)} logs")

for cat in ["per_frame", "loop"]:
    print(f"\n--- {cat.upper()} SOURCES ---")
    counts = defaultdict(int)
    for e in logs_by_cat[cat]:
        counts[f"{e['file']}::{e['method']} {e['tag']}"] += 1
    for k, v in sorted(counts.items(), key=lambda x: x[1], reverse=True):
        print(f"{k}: {v} logs")
