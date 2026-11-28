// Turns a Claude reply (markdown) into plain prose fit for reading aloud.
export function toSpeakable(md, maxChars = 1500) {
  let t = String(md ?? '')
    .replace(/^[ \t]*(`{3,}|~{3,})[^\n]*\n[\s\S]*?(?:^[ \t]*\1[`~]*[ \t]*$|(?![\s\S]))/gm, ' ') // fenced code (``` or ~~~, any length, or never closed)
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

/**
 * For the app, which lays out markdown itself and never reads the markup: keeps headings, lists and emphasis, and
 * drops what should not be read aloud (code blocks, tables, images, bare links), trimmed to about maxChars.
 */
export function toReadableMarkdown(md, maxChars = 1500) {
  let t = String(md ?? '')
    .replace(/^[ \t]*(`{3,}|~{3,})[^\n]*\n[\s\S]*?(?:^[ \t]*\1[`~]*[ \t]*$|(?![\s\S]))/gm, ' ') // fenced code, any length, or never closed
    .replace(/!\[[^\]]*\]\([^)]*\)/g, ' ')
    .replace(/\[([^\]]*)\]\([^)]*\)/g, '$1')
    .replace(/https?:\/\/\S+/g, ' ')
    .replace(/^[ \t]*\|.*\|[ \t]*$/gm, ' ')
    .replace(/[ \t]+$/gm, '')
    .replace(/\n{3,}/g, '\n\n')
    .trim();
  if (t.length > maxChars) {
    const cut = t.slice(0, maxChars);
    const end = Math.max(cut.lastIndexOf('. '), cut.lastIndexOf('\n'));
    t = (end > maxChars / 2 ? cut.slice(0, end + 1) : cut).trim();
  }
  return t;
}

/**
 * Takes a fenced block tagged `tts-questions` (one question per line, a leading "-" or number is fine) out of a reply.
 * The plugin passes those to the app as the questions to wait on; the app itself never decides what is a question.
 */
export function extractQuestions(md) {
  const questions = [];
  const text = String(md ?? '').replace(/^[ \t]*```tts-questions[^\n]*\n([\s\S]*?)^[ \t]*```[ \t]*$/gm, (_, body) => {
    for (const line of body.split('\n')) {
      const q = line.replace(/^\s*(?:[-*+]|\d+[.)])\s+/, '').trim();
      if (q) questions.push(q);
    }
    return '';
  });
  return { text: text.trim(), questions };
}
