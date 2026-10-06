namespace GameServer.Navigation
{
    // XZ 평면을 정사각형 칸으로 나눠 칸마다 "걸어갈 수 있는지"를 담은 이동 가능 격자. Unity가 맵 프리팹의 콜라이더(벽/기둥/가구)를
    // 조사해 내보낸 NavGrids/{mapId}.json을 NavGridCatalog가 읽어 만든다. 서버 몬스터 이동에는 물리 엔진이 없어서, 이 격자가
    // 가구와 벽을 통과하지 못하게 하는 유일한 근거다.
    public sealed class NavGrid
    {
        private const float Epsilon = 1e-3f;

        // A* 한 번에 펼칠 수 있는 최대 칸 수 - 도달 불가능한 목표로 한 틱이 오래 걸리는 것을 막는 안전장치.
        private const int MaxExpandedNodes = 40000;

        private readonly bool[] _walkable;

        public float CellSize { get; }
        public float OriginX { get; }
        public float OriginZ { get; }
        public int Width { get; }
        public int Height { get; }

        public NavGrid(float cellSize, float originX, float originZ, int width, int height, bool[] walkable)
        {
            if (cellSize <= 0f) throw new ArgumentOutOfRangeException(nameof(cellSize));
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "격자 크기는 1 이상이어야 합니다.");
            if (walkable.Length != width * height) throw new ArgumentException("walkable 길이가 width*height와 다릅니다.", nameof(walkable));

            CellSize = cellSize;
            OriginX = originX;
            OriginZ = originZ;
            Width = width;
            Height = height;
            _walkable = walkable;
        }

        // 행 문자열로 만든다. rows[z]의 x번째 글자가 칸(x, z)이고, '.'은 이동 가능, '#'은 이동 불가다.
        public static NavGrid FromRows(float cellSize, float originX, float originZ, IReadOnlyList<string> rows)
        {
            if (rows.Count == 0) throw new ArgumentException("rows가 비어 있습니다.", nameof(rows));

            int width = rows[0].Length;
            var walkable = new bool[width * rows.Count];
            for (int z = 0; z < rows.Count; z++)
            {
                if (rows[z].Length != width)
                {
                    throw new ArgumentException($"rows[{z}]의 길이({rows[z].Length})가 첫 행({width})과 다릅니다.", nameof(rows));
                }

                for (int x = 0; x < width; x++)
                {
                    char c = rows[z][x];
                    if (c != '.' && c != '#')
                    {
                        throw new ArgumentException($"rows[{z}][{x}]의 글자 '{c}'는 '.' 또는 '#'이어야 합니다.", nameof(rows));
                    }

                    walkable[z * width + x] = c == '.';
                }
            }

            return new NavGrid(cellSize, originX, originZ, width, rows.Count, walkable);
        }

        public bool IsWalkable(int cx, int cz) => cx >= 0 && cz >= 0 && cx < Width && cz < Height && _walkable[cz * Width + cx];

        public bool IsWalkableAt(float x, float z) => TryWorldToCell(x, z, out int cx, out int cz) && IsWalkable(cx, cz);

        public bool TryWorldToCell(float x, float z, out int cx, out int cz)
        {
            cx = (int)MathF.Floor((x - OriginX) / CellSize);
            cz = (int)MathF.Floor((z - OriginZ) / CellSize);
            return cx >= 0 && cz >= 0 && cx < Width && cz < Height;
        }

        public (float X, float Z) CellCenter(int cx, int cz) =>
            (OriginX + (cx + 0.5f) * CellSize, OriginZ + (cz + 0.5f) * CellSize);

        // 이동 불가 칸을 몸통 반경만큼 부풀린 새 격자를 만든다. 몬스터를 "점"으로 취급하는 경로 탐색이 몸집 있는 몬스터도
        // 기둥/가구 모서리에 끼지 않고 지나가게 하기 위해서다(칸 중심끼리의 거리가 radius 이하인 칸까지 막는다).
        public NavGrid Dilate(float radius)
        {
            float radiusCells = radius / CellSize;
            int reach = (int)MathF.Ceiling(radiusCells);
            if (reach <= 0)
            {
                return this;
            }

            var result = (bool[])_walkable.Clone();
            for (int z = 0; z < Height; z++)
            {
                for (int x = 0; x < Width; x++)
                {
                    if (_walkable[z * Width + x])
                    {
                        continue;
                    }

                    for (int dz = -reach; dz <= reach; dz++)
                    {
                        for (int dx = -reach; dx <= reach; dx++)
                        {
                            int nx = x + dx;
                            int nz = z + dz;
                            if (nx < 0 || nz < 0 || nx >= Width || nz >= Height)
                            {
                                continue;
                            }

                            if (MathF.Sqrt(dx * dx + dz * dz) <= radiusCells + Epsilon)
                            {
                                result[nz * Width + nx] = false;
                            }
                        }
                    }
                }
            }

            return new NavGrid(CellSize, OriginX, OriginZ, Width, Height, result);
        }

        // (x, z)가 이동 가능하면 그대로, 아니면 가장 가까운 이동 가능 칸의 중심을 돌려준다. maxRing칸 안에 없으면 false.
        public bool TrySnapToWalkable(float x, float z, int maxRing, out float snappedX, out float snappedZ)
        {
            snappedX = x;
            snappedZ = z;

            if (!TryWorldToCell(x, z, out int cx, out int cz))
            {
                return false;
            }

            if (IsWalkable(cx, cz))
            {
                return true;
            }

            float bestDistance = float.MaxValue;
            bool found = false;
            for (int ring = 1; ring <= maxRing; ring++)
            {
                for (int dz = -ring; dz <= ring; dz++)
                {
                    for (int dx = -ring; dx <= ring; dx++)
                    {
                        // 이번 링의 테두리 칸만 본다(안쪽은 이전 링에서 이미 확인했다).
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != ring || !IsWalkable(cx + dx, cz + dz))
                        {
                            continue;
                        }

                        (float centerX, float centerZ) = CellCenter(cx + dx, cz + dz);
                        float distance = (centerX - x) * (centerX - x) + (centerZ - z) * (centerZ - z);
                        if (distance < bestDistance)
                        {
                            bestDistance = distance;
                            snappedX = centerX;
                            snappedZ = centerZ;
                            found = true;
                        }
                    }
                }

                // 링 단위로 넓혀 가며 처음 찾은 링에서 멈춘다 - 모서리에서는 바로 바깥 링의 칸이 조금 더 가까울 수 있지만,
                // 몬스터 위치를 장애물 밖으로 살짝 옮기는 용도라 정확한 최근접까지는 필요 없다.
                if (found)
                {
                    break;
                }
            }

            return found;
        }

        // 두 점 사이를 일직선으로 걸을 수 있는지(경로 위의 모든 칸이 이동 가능하고 allowed도 통과하는지).
        public bool HasLineOfSight(float x0, float z0, float x1, float z1, Func<float, float, bool>? allowed = null)
        {
            float dx = x1 - x0;
            float dz = z1 - z0;
            float distance = MathF.Sqrt(dx * dx + dz * dz);

            // 칸 크기의 1/4 간격으로 표본을 찍어 칸 모서리를 스치며 빠져나가는 것을 막는다.
            int steps = Math.Max(1, (int)MathF.Ceiling(distance / (CellSize / 4f)));
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                float x = x0 + dx * t;
                float z = z0 + dz * t;
                if (!IsWalkableAt(x, z) || (allowed != null && !allowed(x, z)))
                {
                    return false;
                }
            }

            return true;
        }

        // 시작점에서 목표점까지의 경로(중간 꺾이는 지점들, 마지막은 목표)를 A*로 찾는다. 시작/목표가 이동 불가 칸이면 가까운
        // 이동 가능 칸으로 옮겨서 찾는다(플레이어가 가구 바로 옆에 서 있으면 부풀린 격자에서는 그 칸이 막혀 있기 때문이다).
        // allowed는 칸 중심이 활동 영역 안인지 같은 추가 조건이다. 길이 없으면 false.
        public bool TryFindPath(float startX, float startZ, float goalX, float goalZ, Func<float, float, bool>? allowed, out List<(float X, float Z)> path)
        {
            path = new List<(float X, float Z)>();

            const int SnapRing = 3;
            if (!TrySnapToWalkable(startX, startZ, SnapRing, out float sx, out float sz)
                || !TrySnapToWalkable(goalX, goalZ, SnapRing, out float gx, out float gz))
            {
                return false;
            }

            TryWorldToCell(sx, sz, out int startCx, out int startCz);
            TryWorldToCell(gx, gz, out int goalCx, out int goalCz);

            bool IsOpen(int cx, int cz)
            {
                if (!IsWalkable(cx, cz))
                {
                    return false;
                }

                if (allowed == null)
                {
                    return true;
                }

                (float x, float z) = CellCenter(cx, cz);
                return allowed(x, z);
            }

            if (!IsOpen(startCx, startCz) && !(startCx == goalCx && startCz == goalCz))
            {
                // 시작 칸이 영역 밖이면(영역 가장자리) 영역 안으로 들어가는 이동은 허용하지 않는다.
                return false;
            }

            if (!IsOpen(goalCx, goalCz))
            {
                return false;
            }

            int Index(int cx, int cz) => cz * Width + cx;
            int start = Index(startCx, startCz);
            int goal = Index(goalCx, goalCz);

            var cameFrom = new Dictionary<int, int>();
            var costSoFar = new Dictionary<int, float> { [start] = 0f };
            var frontier = new PriorityQueue<int, float>();
            frontier.Enqueue(start, 0f);

            float Heuristic(int cx, int cz)
            {
                float dx = Math.Abs(cx - goalCx);
                float dz = Math.Abs(cz - goalCz);
                return (dx + dz) + (MathF.Sqrt(2f) - 2f) * Math.Min(dx, dz);
            }

            int expanded = 0;
            bool reached = false;
            while (frontier.TryDequeue(out int current, out _))
            {
                if (current == goal)
                {
                    reached = true;
                    break;
                }

                if (++expanded > MaxExpandedNodes)
                {
                    break;
                }

                int cx = current % Width;
                int cz = current / Width;
                float currentCost = costSoFar[current];

                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0)
                        {
                            continue;
                        }

                        int nx = cx + dx;
                        int nz = cz + dz;
                        if (!IsOpen(nx, nz))
                        {
                            continue;
                        }

                        // 대각선 이동은 양옆 칸 중 하나라도 막혀 있으면 모서리를 베고 지나가는 것이라 허용하지 않는다.
                        if (dx != 0 && dz != 0 && (!IsOpen(cx + dx, cz) || !IsOpen(cx, cz + dz)))
                        {
                            continue;
                        }

                        float newCost = currentCost + (dx != 0 && dz != 0 ? MathF.Sqrt(2f) : 1f);
                        int next = Index(nx, nz);
                        if (!costSoFar.TryGetValue(next, out float oldCost) || newCost < oldCost)
                        {
                            costSoFar[next] = newCost;
                            cameFrom[next] = current;
                            frontier.Enqueue(next, newCost + Heuristic(nx, nz));
                        }
                    }
                }
            }

            if (!reached)
            {
                return false;
            }

            var cells = new List<(float X, float Z)>();
            for (int node = goal; node != start; node = cameFrom[node])
            {
                cells.Add(CellCenter(node % Width, node / Width));
            }

            cells.Reverse();

            // 시작과 목표가 같은 칸이면 이동할 칸이 없으므로 목표 칸 자체를 유일한 경유점으로 둔다.
            if (cells.Count == 0)
            {
                cells.Add((gx, gz));
            }

            // 목표가 원래 이동 가능한 칸이었다면 칸 중심 대신 정확한 목표 좌표로 끝낸다.
            if (cells.Count > 0 && IsWalkableAt(goalX, goalZ) && IsOpen(goalCx, goalCz))
            {
                cells[^1] = (goalX, goalZ);
            }

            path = Smooth(startX, startZ, cells, allowed);
            return true;
        }

        // 칸 중심을 따라 꺾이는 경로를, 일직선으로 갈 수 있는 구간은 건너뛰어 짧게 줄인다.
        private List<(float X, float Z)> Smooth(float startX, float startZ, List<(float X, float Z)> cells, Func<float, float, bool>? allowed)
        {
            var smoothed = new List<(float X, float Z)>();
            float currentX = startX;
            float currentZ = startZ;
            int index = 0;

            while (index < cells.Count)
            {
                int farthest = index;
                for (int candidate = cells.Count - 1; candidate > index; candidate--)
                {
                    if (HasLineOfSight(currentX, currentZ, cells[candidate].X, cells[candidate].Z, allowed))
                    {
                        farthest = candidate;
                        break;
                    }
                }

                smoothed.Add(cells[farthest]);
                currentX = cells[farthest].X;
                currentZ = cells[farthest].Z;
                index = farthest + 1;
            }

            return smoothed;
        }
    }
}
