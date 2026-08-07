// Web worker helper for offloading heavy markdown preprocessing and syntax tokenization

self.onmessage = (event: MessageEvent<{ content: string; id: string }>) => {
  const { content, id } = event.data;
  if (!content) {
    self.postMessage({ id, processedContent: "", blocks: [] });
    return;
  }

  // Pre-process code blocks or large markdown documents in background worker
  const codeBlockRegex = /```(\w+)?\n([\s\S]*?)```/g;
  const blocks: Array<{ language: string; code: string }> = [];
  let match: RegExpExecArray | null;

  while ((match = codeBlockRegex.exec(content)) !== null) {
    blocks.push({
      language: match[1] || "plaintext",
      code: match[2]
    });
  }

  self.postMessage({
    id,
    processedContent: content,
    codeBlocksCount: blocks.length,
    blocks
  });
};
