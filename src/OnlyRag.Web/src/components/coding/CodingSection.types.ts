// CodingSection types
export type CodingMode = "ask" | "plan" | "full";

export type FileAction = {
  file: string;
  action: "write" | "delete";
  code?: string;
  applied?: boolean;
};
