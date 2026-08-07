export type EntityGraphNode = {
  nodeId: string;
  documentId: string;
  chunkId: string;
  name: string;
  type: string;
  description: string;
};

export type EntityGraphEdge = {
  edgeId: string;
  sourceNodeId: string;
  targetNodeId: string;
  relationType: string;
  weight: number;
  chunkId: string;
};

export type GraphRetrievalResult = {
  nodes: EntityGraphNode[];
  edges: EntityGraphEdge[];
  relatedChunkIds: string[];
  score: number;
};

export type GraphSearchRequest = {
  query: string;
  maxHops?: number;
  maxNodes?: number;
};
