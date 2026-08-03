// CodingSection types
export type FileAction = {
  file: string;
  action: "write" | "delete";
  code?: string;
  applied?: boolean;
};
