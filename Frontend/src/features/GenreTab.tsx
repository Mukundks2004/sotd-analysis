import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  forceCollide,
  forceLink,
  forceManyBody,
  forceSimulation,
  forceX,
  forceY,
  type Simulation,
  type SimulationLinkDatum,
  type SimulationNodeDatum,
} from 'd3';
import { Box, Button, CircularProgress, Menu, MenuItem, Paper, Stack, TextField, Typography } from '@mui/material';
import { api, type GenreEdge, type GenreGraph } from '../api';
import { useToast } from '../ui/Toast';

interface Node extends SimulationNodeDatum {
  id: string;
  name: string;
  /** Rows down from a root, which is what pulls roots to the top and children beneath them. */
  depth: number;
}

interface Link extends SimulationLinkDatum<Node> {
  parentId: string;
  childId: string;
}

const NODE_RADIUS = 34;
const ROW_HEIGHT = 110;

/** How far below a root each node sits, guarding against a cycle the backend somehow let through. */
function depths(nodeIds: string[], edges: GenreEdge[]): Map<string, number> {
  const parentOf = new Map(edges.map((edge) => [edge.childId, edge.parentId]));
  const result = new Map<string, number>();

  for (const id of nodeIds) {
    let depth = 0;
    let at = id;
    const seen = new Set<string>([id]);
    for (;;) {
      const parent = parentOf.get(at);
      if (!parent || seen.has(parent)) break;
      seen.add(parent);
      at = parent;
      depth++;
    }
    result.set(id, depth);
  }
  return result;
}

export function GenreTab() {
  const [graph, setGraph] = useState<GenreGraph>({ nodes: [], edges: [] });
  const [loading, setLoading] = useState(true);
  // The simulation mutates these objects in place; a fresh array per tick is what makes React redraw.
  const [nodes, setNodes] = useState<Node[]>([]);
  const [links, setLinks] = useState<Link[]>([]);

  const svgRef = useRef<SVGSVGElement | null>(null);
  const nodesRef = useRef<Node[]>([]);
  const simRef = useRef<Simulation<Node, Link> | null>(null);
  const sizeRef = useRef({ width: 900, height: 600 });

  const [dragging, setDragging] = useState<string | null>(null);
  const [linkFrom, setLinkFrom] = useState<string | null>(null);
  const [pointer, setPointer] = useState<{ x: number; y: number } | null>(null);
  const [hoverNode, setHoverNode] = useState<string | null>(null);
  const [creating, setCreating] = useState<{ x: number; y: number } | null>(null);
  const [newName, setNewName] = useState('');
  const [edgeMenu, setEdgeMenu] = useState<{ edge: GenreEdge; x: number; y: number } | null>(null);
  const [busy, setBusy] = useState(false);

  const toast = useToast();

  const load = useCallback(async () => {
    try {
      setGraph(await api.genreGraph());
    } catch (error) {
      const failure = error as { message?: string; detail?: string };
      toast.error(failure.message ?? 'Could not load the genre graph.', failure.detail);
    } finally {
      setLoading(false);
    }
  }, [toast]);

  useEffect(() => {
    void load();
  }, [load]);

  // Track the drawing area so the forces centre on whatever space is actually available.
  useEffect(() => {
    const element = svgRef.current;
    if (!element) return;
    const observer = new ResizeObserver(([entry]) => {
      sizeRef.current = { width: entry.contentRect.width, height: entry.contentRect.height };
      simRef.current?.alpha(0.3).restart();
    });
    observer.observe(element);
    return () => observer.disconnect();
  }, []);

  // Rebuild the simulation whenever the graph changes, keeping positions of nodes that still exist so the
  // picture does not jump every time an edge is added.
  useEffect(() => {
    const previous = new Map(nodesRef.current.map((node) => [node.id, node]));
    const depthOf = depths(
      graph.nodes.map((node) => node.id),
      graph.edges,
    );
    const { width, height } = sizeRef.current;

    const simNodes: Node[] = graph.nodes.map((node, index) => {
      const old = previous.get(node.id);
      return {
        id: node.id,
        name: node.name,
        depth: depthOf.get(node.id) ?? 0,
        x: old?.x ?? width / 2 + Math.cos(index) * 120,
        y: old?.y ?? height / 3 + Math.sin(index) * 120,
        vx: old?.vx ?? 0,
        vy: old?.vy ?? 0,
        fx: old?.fx ?? null,
        fy: old?.fy ?? null,
      };
    });

    const byId = new Map(simNodes.map((node) => [node.id, node]));
    const simLinks: Link[] = graph.edges
      .filter((edge) => byId.has(edge.parentId) && byId.has(edge.childId))
      .map((edge) => ({
        parentId: edge.parentId,
        childId: edge.childId,
        source: byId.get(edge.parentId)!,
        target: byId.get(edge.childId)!,
      }));

    nodesRef.current = simNodes;
    setNodes(simNodes);
    setLinks(simLinks);

    simRef.current?.stop();
    const simulation = forceSimulation<Node, Link>(simNodes)
      .force('charge', forceManyBody<Node>().strength(-900))
      .force(
        'link',
        forceLink<Node, Link>(simLinks)
          .id((node) => node.id)
          .distance(ROW_HEIGHT)
          .strength(0.6),
      )
      .force('centreX', forceX<Node>(() => sizeRef.current.width / 2).strength(0.04))
      // Roots are pulled to the top and every child settles one row below its parent.
      .force('rows', forceY<Node>((node) => 80 + node.depth * ROW_HEIGHT).strength(0.45))
      .force('collide', forceCollide<Node>(NODE_RADIUS + 12))
      .alpha(0.9)
      .on('tick', () => setNodes([...simNodes]));

    simRef.current = simulation;
    return () => {
      simulation.stop();
    };
  }, [graph]);

  function pointAt(event: { clientX: number; clientY: number }) {
    const box = svgRef.current?.getBoundingClientRect();
    return { x: event.clientX - (box?.left ?? 0), y: event.clientY - (box?.top ?? 0) };
  }

  function nodeAt(x: number, y: number): Node | undefined {
    return nodesRef.current.find((node) => Math.hypot((node.x ?? 0) - x, (node.y ?? 0) - y) <= NODE_RADIUS);
  }

  function onPointerMove(event: React.PointerEvent<SVGSVGElement>) {
    const at = pointAt(event);
    if (dragging) {
      const node = nodesRef.current.find((candidate) => candidate.id === dragging);
      if (node) {
        node.fx = at.x;
        node.fy = at.y;
        simRef.current?.alphaTarget(0.25).restart();
      }
      return;
    }
    if (linkFrom) {
      setPointer(at);
      setHoverNode(nodeAt(at.x, at.y)?.id ?? null);
    }
  }

  async function onPointerUp(event: React.PointerEvent<SVGSVGElement>) {
    if (dragging) {
      const node = nodesRef.current.find((candidate) => candidate.id === dragging);
      // Let go of the node so the forces can settle it again.
      if (node) {
        node.fx = null;
        node.fy = null;
      }
      simRef.current?.alphaTarget(0);
      setDragging(null);
      return;
    }

    if (linkFrom) {
      const at = pointAt(event);
      const target = nodeAt(at.x, at.y);
      const parentId = linkFrom;
      setLinkFrom(null);
      setPointer(null);
      setHoverNode(null);
      if (!target || target.id === parentId) return;

      setBusy(true);
      try {
        setGraph(await api.setGenreParent(parentId, target.id));
        toast.success('Parent set.');
      } catch (error) {
        const failure = error as { message?: string; detail?: string };
        toast.error(failure.message ?? 'Could not set the parent.', failure.detail);
      } finally {
        setBusy(false);
      }
    }
  }

  async function createGenre() {
    const name = newName.trim();
    if (name === '') return;
    setBusy(true);
    try {
      await api.createGenre(name);
      setNewName('');
      setCreating(null);
      toast.success('Genre created.');
      await load();
    } catch (error) {
      const failure = error as { message?: string; detail?: string };
      toast.error(failure.message ?? 'Could not create the genre.', failure.detail);
    } finally {
      setBusy(false);
    }
  }

  async function removeParent(edge: GenreEdge) {
    setEdgeMenu(null);
    setBusy(true);
    try {
      setGraph(await api.removeGenreParent(edge.childId));
      toast.success('Parent removed.');
    } catch (error) {
      const failure = error as { message?: string; detail?: string };
      toast.error(failure.message ?? 'Could not remove the parent.', failure.detail);
    } finally {
      setBusy(false);
    }
  }

  // Escape closes the create box, matching the click-away behaviour.
  useEffect(() => {
    if (!creating) return;
    function onKey(event: KeyboardEvent) {
      if (event.key === 'Escape') setCreating(null);
    }
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [creating]);

  const nodeById = useMemo(() => new Map(nodes.map((node) => [node.id, node])), [nodes]);
  const fromNode = linkFrom ? nodes.find((node) => node.id === linkFrom) : undefined;

  return (
    <Stack spacing={2}>
      <Stack direction="row" spacing={2} sx={{ alignItems: 'center' }}>
        <Typography variant="body2" color="text.secondary">
          Drag a node to move it. Drag from the small handle on a node onto another node to make the first the
          parent. Right-click empty space to add a genre, or right-click an arrow to remove that parent.
        </Typography>
        <Box sx={{ flexGrow: 1 }} />
        {(busy || loading) && <CircularProgress size={18} />}
        <Typography variant="body2" color="text.secondary">
          {nodes.length} genres
        </Typography>
      </Stack>

      <Paper variant="outlined" sx={{ position: 'relative', overflow: 'hidden' }}>
        <svg
          ref={svgRef}
          width="100%"
          height={620}
          style={{ display: 'block', touchAction: 'none', background: '#fafafa' }}
          onPointerMove={onPointerMove}
          onPointerUp={(event) => void onPointerUp(event)}
          onPointerLeave={() => {
            setLinkFrom(null);
            setPointer(null);
            setDragging(null);
          }}
          onContextMenu={(event) => {
            event.preventDefault();
            const at = pointAt(event);
            // Only empty space opens the create box; arrows handle their own menu.
            if (nodeAt(at.x, at.y)) return;
            setCreating(at);
            setNewName('');
          }}
        >
          <defs>
            <marker
              id="genre-arrow"
              viewBox="0 0 10 10"
              refX="10"
              refY="5"
              markerWidth="7"
              markerHeight="7"
              orient="auto-start-reverse"
            >
              <path d="M 0 0 L 10 5 L 0 10 z" fill="#5c6bc0" />
            </marker>
          </defs>

          {links.map((link) => {
            const source = link.source as Node;
            const target = link.target as Node;
            const dx = (target.x ?? 0) - (source.x ?? 0);
            const dy = (target.y ?? 0) - (source.y ?? 0);
            const length = Math.hypot(dx, dy) || 1;
            // Stop the line at the edge of the circle so the arrowhead is not hidden underneath it.
            const x2 = (target.x ?? 0) - (dx / length) * (NODE_RADIUS + 3);
            const y2 = (target.y ?? 0) - (dy / length) * (NODE_RADIUS + 3);
            return (
              <line
                key={`${link.parentId}-${link.childId}`}
                x1={source.x}
                y1={source.y}
                x2={x2}
                y2={y2}
                stroke="#5c6bc0"
                strokeWidth={10}
                strokeOpacity={0.001}
                markerEnd="url(#genre-arrow)"
                style={{ cursor: 'context-menu' }}
                onContextMenu={(event) => {
                  event.preventDefault();
                  event.stopPropagation();
                  setEdgeMenu({
                    edge: { parentId: link.parentId, childId: link.childId },
                    x: event.clientX,
                    y: event.clientY,
                  });
                }}
              />
            );
          })}
          {/* A second thin pass draws the visible line; the thick one above only catches right-clicks. */}
          {links.map((link) => {
            const source = link.source as Node;
            const target = link.target as Node;
            const dx = (target.x ?? 0) - (source.x ?? 0);
            const dy = (target.y ?? 0) - (source.y ?? 0);
            const length = Math.hypot(dx, dy) || 1;
            return (
              <line
                key={`v-${link.parentId}-${link.childId}`}
                x1={source.x}
                y1={source.y}
                x2={(target.x ?? 0) - (dx / length) * (NODE_RADIUS + 3)}
                y2={(target.y ?? 0) - (dy / length) * (NODE_RADIUS + 3)}
                stroke="#5c6bc0"
                strokeWidth={2}
                markerEnd="url(#genre-arrow)"
                pointerEvents="none"
              />
            );
          })}

          {fromNode && pointer && (
            <line
              x1={fromNode.x}
              y1={fromNode.y}
              x2={pointer.x}
              y2={pointer.y}
              stroke="#9fa8da"
              strokeWidth={2}
              strokeDasharray="5 4"
              pointerEvents="none"
            />
          )}

          {nodes.map((node) => (
            <g key={node.id}>
              <circle
                cx={node.x}
                cy={node.y}
                r={NODE_RADIUS}
                fill={hoverNode === node.id && linkFrom ? '#c5cae9' : node.depth === 0 ? '#e8eaf6' : '#fff'}
                stroke={linkFrom === node.id ? '#3949ab' : '#5c6bc0'}
                strokeWidth={node.depth === 0 ? 3 : 1.5}
                style={{ cursor: 'grab' }}
                onPointerDown={(event) => {
                  event.preventDefault();
                  event.currentTarget.ownerSVGElement?.setPointerCapture(event.pointerId);
                  // Shift-drag is the keyboard-free alternative to the handle for drawing an arrow.
                  if (event.shiftKey) {
                    setLinkFrom(node.id);
                    setPointer({ x: node.x ?? 0, y: node.y ?? 0 });
                  } else {
                    setDragging(node.id);
                  }
                }}
              />
              <text
                x={node.x}
                y={(node.y ?? 0) + 4}
                textAnchor="middle"
                fontSize={12}
                pointerEvents="none"
                style={{ userSelect: 'none' }}
              >
                {node.name.length > 12 ? `${node.name.slice(0, 11)}\u2026` : node.name}
              </text>
              <title>{node.name}</title>

              <circle
                cx={(node.x ?? 0) + NODE_RADIUS}
                cy={node.y}
                r={7}
                fill="#3949ab"
                stroke="#fff"
                strokeWidth={2}
                style={{ cursor: 'crosshair' }}
                onPointerDown={(event) => {
                  event.preventDefault();
                  event.stopPropagation();
                  event.currentTarget.ownerSVGElement?.setPointerCapture(event.pointerId);
                  setLinkFrom(node.id);
                  setPointer({ x: node.x ?? 0, y: node.y ?? 0 });
                }}
              >
                <title>Drag onto another genre to make it a child</title>
              </circle>
            </g>
          ))}
        </svg>

        {creating && (
          <>
            {/* Clicking anywhere outside the box closes it. */}
            <Box
              onPointerDown={() => setCreating(null)}
              sx={{ position: 'absolute', inset: 0 }}
            />
            <Paper
              elevation={6}
              sx={{
                position: 'absolute',
                left: Math.min(creating.x, (sizeRef.current.width || 900) - 280),
                top: creating.y,
                p: 1.5,
                width: 260,
              }}
              onPointerDown={(event) => event.stopPropagation()}
            >
              <Stack spacing={1}>
                <TextField
                  autoFocus
                  size="small"
                  label="New genre"
                  value={newName}
                  onChange={(event) => setNewName(event.target.value)}
                  onKeyDown={(event) => {
                    if (event.key === 'Enter') void createGenre();
                  }}
                />
                <Stack direction="row" spacing={1} sx={{ justifyContent: 'flex-end' }}>
                  <Button size="small" onClick={() => setCreating(null)}>
                    Cancel
                  </Button>
                  <Button size="small" variant="contained" disabled={busy} onClick={() => void createGenre()}>
                    Create
                  </Button>
                </Stack>
              </Stack>
            </Paper>
          </>
        )}
      </Paper>

      <Menu
        open={edgeMenu !== null}
        onClose={() => setEdgeMenu(null)}
        anchorReference="anchorPosition"
        anchorPosition={edgeMenu ? { left: edgeMenu.x, top: edgeMenu.y } : undefined}
      >
        <MenuItem onClick={() => edgeMenu && void removeParent(edgeMenu.edge)}>
          Remove parent
          {edgeMenu && nodeById.get(edgeMenu.edge.childId) ? ` of ${nodeById.get(edgeMenu.edge.childId)!.name}` : ''}
        </MenuItem>
      </Menu>
    </Stack>
  );
}
