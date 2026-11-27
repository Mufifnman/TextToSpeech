// Turns a Claude reply (markdown) into plain prose fit for reading aloud.
export function toSpeakable(md, maxChars = 1500) {
  let t = String(md ?? '')
    .replace(/```[\s\S]*?```/g, ' ')            // fenced code
    .replace(/`([^`]*)`/g, '$1')                // inline code
    .replace(/!\[[^\]]*\]\([^)]*\)/g, ' ')      // images
    .replace(/\[([^\]]*)\]\([^)]*\)/g, '$1')    // links keep their text
    .replace(/https?:\/\/\S+/g, ' ')
    .replace(/^[ \t]*\|.*\|[ \t]*$/gm, ' ')           // table rows
    .replace(/^[ \t]{0,3}#{1,6}[ \t]*/gm, '')
    .replace(/^[ \t]*[-*+][ \t]+/gm, '')
    .replace(/^[ \t]*\d+\.[ \t]+/gm, '')
    .replace(/[*_~>]+/g, '')
    .replace(/[ \t]+/g, ' ')
    .replace(/^ +| +$/gm, '')
    .replace(/\n{2,}/g, '\n\n')
    .trim();
  if (t.length > maxChars) {
    const cut = t.slice(0, maxChars);
    const end = Math.max(cut.lastIndexOf('. '), cut.lastIndexOf('\n'));
    t = (end > maxChars / 2 ? cut.slice(0, end + 1) : cut).trim();
  }
  return t;
}

/** Text of the last assistant message in a Claude Code transcript (JSONL). */
export function lastAssistantText(jsonl) {
  const lines = String(jsonl).split('\n').filter(Boolean);
  for (let i = lines.length - 1; i >= 0; i--) {
    let e;
    try { e = JSON.parse(lines[i]); } catch { continue; }
    if (e.type !== 'assistant' && e.message?.role !== 'assistant') continue;
    const c = e.message?.content;
    const text = typeof c === 'string' ? c : (c ?? []).filter((b) => b.type === 'text').map((b) => b.text).join('\n');
    if (text.trim()) return text;
  }
  return '';
}
