export type WorkspaceConfig = {
  rootPath: string | null;
  isAuthorized: boolean;
  canRead: boolean;
  canWrite: boolean;
  fileCount: number;
  lastVerifiedAt: string | null;
};

export type SelectWorkspaceRequest = {
  folderPath: string;
};

export type WorkspaceFileItem = {
  relativePath: string;
  fullPath: string;
  isDirectory: boolean;
  sizeBytes: number;
  lastModified: string;
};

export type ReadWorkspaceFileRequest = {
  relativePath: string;
};

export type ReadWorkspaceFileResponse = {
  relativePath: string;
  content: string;
  sizeBytes: number;
  language: string;
};

export type WriteWorkspaceFileRequest = {
  relativePath: string;
  content: string;
};

export type WriteWorkspaceFileResponse = {
  relativePath: string;
  success: boolean;
  message: string;
};

export type OpenExternalFileRequest = {
  path: string;
};

export type PickWorkspaceFolderResponse = WorkspaceConfig & {
  cancelled?: boolean;
};

