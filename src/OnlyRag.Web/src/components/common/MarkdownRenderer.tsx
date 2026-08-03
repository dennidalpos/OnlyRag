import ReactMarkdown from "react-markdown";
import rehypeHighlight from "rehype-highlight";
import remarkGfm from "remark-gfm";

type MarkdownRendererProps = {
  content: string;
  className?: string;
};

export function MarkdownRenderer({ content, className = "" }: MarkdownRendererProps) {
  return (
    <div className={`prose dark:prose-invert max-w-none text-inherit leading-relaxed ${className}`}>
      <ReactMarkdown
        remarkPlugins={[remarkGfm]}
        rehypePlugins={[rehypeHighlight]}
        components={{
          pre: ({ children, ...props }) => (
            <pre
              {...props}
              className="my-2 p-3 overflow-x-auto rounded-lg bg-slate-900 text-slate-100 font-mono text-xs border border-slate-700/60"
            >
              {children}
            </pre>
          ),
          code: ({ className: codeClassName, children, ...props }) => {
            const isInline = !codeClassName && typeof children === "string" && !children.includes("\n");
            if (isInline) {
              return (
                <code
                  {...props}
                  className="px-1.5 py-0.5 rounded bg-slate-800/80 text-amber-300 font-mono text-[0.85em] border border-slate-700/50"
                >
                  {children}
                </code>
              );
            }
            return (
              <code {...props} className={codeClassName}>
                {children}
              </code>
            );
          },
          table: ({ children, ...props }) => (
            <div className="my-3 overflow-x-auto rounded-md border border-slate-700/60">
              <table {...props} className="min-w-full divide-y divide-slate-700/60 text-xs">
                {children}
              </table>
            </div>
          ),
          th: ({ children, ...props }) => (
            <th {...props} className="px-3 py-1.5 bg-slate-800/80 font-semibold text-slate-200 text-left">
              {children}
            </th>
          ),
          td: ({ children, ...props }) => (
            <td {...props} className="px-3 py-1.5 border-t border-slate-800/60 text-slate-300">
              {children}
            </td>
          ),
          p: ({ children, ...props }) => (
            <p {...props} className="mb-2 last:mb-0">
              {children}
            </p>
          ),
          ul: ({ children, ...props }) => (
            <ul {...props} className="list-disc list-inside my-2 space-y-1">
              {children}
            </ul>
          ),
          ol: ({ children, ...props }) => (
            <ol {...props} className="list-decimal list-inside my-2 space-y-1">
              {children}
            </ol>
          ),
          a: ({ children, href, ...props }) => (
            <a
              {...props}
              href={href}
              target="_blank"
              rel="noopener noreferrer"
              className="text-blue-400 hover:text-blue-300 underline underline-offset-2"
            >
              {children}
            </a>
          )
        }}
      >
        {content}
      </ReactMarkdown>
    </div>
  );
}
