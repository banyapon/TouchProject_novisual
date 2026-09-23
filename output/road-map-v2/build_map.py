"""Build an exact road diagram and endpoint graph from one geometry definition."""
from pathlib import Path
import csv
import json
import math
from collections import deque

OUT = Path(__file__).resolve().parent
RADIUS = 18
CENTERS = {
    'C': (256, 256), 'IW': (144, 256), 'IN': (256, 144),
    'IE': (368, 256), 'IS': (256, 368), 'OW': (48, 256),
    'ON': (256, 48), 'OE': (464, 256), 'OS': (256, 464),
}
VECTORS = {'W': (-1, 0), 'N': (0, -1), 'E': (1, 0), 'S': (0, 1)}
ARMS = {name: {} for name in CENTERS}
ROADS = []


def port(junction, side):
    x, y = CENTERS[junction]
    dx, dy = VECTORS[side]
    return [x + RADIUS * dx, y + RADIUS * dy]


def road_record(points, kind, direction=-1):
    road = dict(id=len(ROADS) + 1, kind=kind, direction=direction,
                allowedEntryNodes=[0, 1] if direction == -1 else [direction],
                nodeS=[], nodeE=[], defaultRoadS=0, defaultRoadE=0,
                points=points)
    ROADS.append(road)
    return road


def main(start, end, corners=()):
    a, sa = start
    b, sb = end
    road = road_record([port(a, sa), *map(list, corners), port(b, sb)], 'main')
    road.update(startJunction=a, endJunction=b)
    ARMS[a][sa] = (road['id'], 0)
    ARMS[b][sb] = (road['id'], 1)


main(('OW', 'E'), ('IW', 'W'))
main(('IE', 'E'), ('OE', 'W'))
main(('ON', 'S'), ('IN', 'N'))
main(('IS', 'S'), ('OS', 'N'))
main(('IW', 'E'), ('C', 'W'))
main(('C', 'E'), ('IE', 'W'))
main(('IN', 'S'), ('C', 'N'))
main(('C', 'S'), ('IS', 'N'))
main(('IW', 'N'), ('IN', 'W'), [(144, 144)])
main(('IN', 'E'), ('IE', 'N'), [(368, 144)])
main(('IE', 'S'), ('IS', 'E'), [(368, 368)])
main(('IS', 'W'), ('IW', 'S'), [(144, 368)])
main(('OW', 'N'), ('ON', 'W'), [(48, 48)])
main(('ON', 'E'), ('OE', 'N'), [(464, 48)])
main(('OE', 'S'), ('OS', 'E'), [(464, 464)])
main(('OS', 'W'), ('OW', 'S'), [(48, 464)])


def endpoint_links(road_id, node):
    return ROADS[road_id - 1]['nodeS' if node == 0 else 'nodeE']


def connect(a, b):
    for source, target in ((a, b), (b, a)):
        item = dict(roadNo=target[0], enterNode=target[1])
        links = endpoint_links(*source)
        if item not in links:
            links.append(item)


JUNCTIONS = []
PAIRS = []
QUADRANTS = [('W', 'N'), ('N', 'E'), ('E', 'S'), ('S', 'W')]
for name, center in CENTERS.items():
    arms = ARMS[name]
    junction = dict(id=name, center=list(center), straightConnections=[], connectorRoads=[])
    JUNCTIONS.append(junction)
    for sa, sb in [('W', 'E'), ('N', 'S')]:
        if sa not in arms or sb not in arms:
            continue
        a, b = arms[sa], arms[sb]
        connect(a, b)
        # These are direct continuations, never separate road IDs.
        junction['straightConnections'].append(dict(
            fromRoadNo=a[0], fromNode=a[1], toRoadNo=b[0], toNode=b[1],
            bidirectional=True, points=[port(name, sa), port(name, sb)]))
        for source, target in ((a, b), (b, a)):
            ROADS[source[0] - 1]['defaultRoadS' if source[1] == 0 else 'defaultRoadE'] = target[0]
    for sa, sb in QUADRANTS:
        if sa not in arms or sb not in arms:
            continue
        a, b = arms[sa], arms[sb]
        p, q = port(name, sa), port(name, sb)
        ids = []
        for direction in (0, 1):
            # Both roads use the same S/E convention. The return road runs E->S.
            offset = 0 if direction == 0 else 5
            control = [center[k] + offset * (VECTORS[sa][k] + VECTORS[sb][k]) for k in (0, 1)]
            points = []
            for i in range(25):
                t = i / 24
                points.append([round((1-t)**2*p[k] + 2*(1-t)*t*control[k] + t*t*q[k], 5) for k in (0, 1)])
            road = road_record(points, 'turn', direction)
            source, target = (a, b) if direction == 0 else (b, a)
            road.update(junction=name, fromRoadNo=source[0], fromNode=source[1],
                        toRoadNo=target[0], toNode=target[1], controlPoint=control)
            connect(a, (road['id'], 0))
            connect(b, (road['id'], 1))
            road['defaultRoadS'] = a[0]
            road['defaultRoadE'] = b[0]
            ids.append(road['id'])
        ROADS[ids[0]-1]['reverseRoad'] = ids[1]
        ROADS[ids[1]-1]['reverseRoad'] = ids[0]
        junction['connectorRoads'].extend(ids)
        PAIRS.append((name, sa, sb, ids))


def permitted(connection):
    return connection['enterNode'] in ROADS[connection['roadNo']-1]['allowedEntryNodes']


for road in ROADS:
    for node, key in [(0, 'nodeS'), (1, 'nodeE')]:
        road[key].sort(key=lambda c: (not permitted(c), c['roadNo']))
        default_key = 'defaultRoadS' if node == 0 else 'defaultRoadE'
        if not road[default_key]:
            legal = [c for c in road[key] if permitted(c)]
            assert legal
            road[default_key] = legal[0]['roadNo']

DOCUMENT = dict(
    schemaVersion=2, canvas=dict(width=512, height=512, origin='top-left', yAxis='down'),
    nodeConvention={'0': 'S / points[0]', '1': 'E / points[-1]'},
    directionConvention={'-1': 'bidirectional', '0': 'S to E only', '1': 'E to S only'},
    notes=[
        'IDs 1-16 are main roads; IDs 17-72 are directed turn connectors.',
        'nodeS/nodeE describe reciprocal physical incidence. Filter destinations by allowedEntryNodes before traversal.',
        'For the opposite turn use reverseRoad; do not travel backward on the same connector ID.',
        'Straight continuations use direct nodeS/nodeE links with no extra road ID. Interpolate the corresponding junction straightConnections points.',
        'enterNode is the destination endpoint, not a travel-direction flag. Depending on the main-road orientation, joins may be 1-0, 0-1, 0-0, or 1-1.',
        'At the central crossing straight roads cross geometrically; switch direction only via a numbered turn connector.',
    ], roads=ROADS, junctions=JUNCTIONS)
(OUT / 'road_data_v2.json').write_text(json.dumps(DOCUMENT, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

# Exact diagram generated from the JSON geometry, independent of the visual mockup.
svg = ['<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512" viewBox="0 0 512 512">',
       '<rect width="512" height="512" fill="black"/>',
       '<g fill="none" stroke="white" stroke-linecap="round" stroke-linejoin="round">']


def polyline(points, width):
    data = ' '.join(f'{p[0]},{p[1]}' for p in points)
    svg.append(f'<polyline points="{data}" stroke-width="{width}"/>')


for road in ROADS[:16]:
    polyline(road['points'], 2.4)
for junction in JUNCTIONS:
    for connection in junction['straightConnections']:
        polyline(connection['points'], 2.4)
for road in ROADS[16:]:
    polyline(road['points'], 1.1)
svg.append('</g>')
svg.append('<g fill="white" font-family="Arial, sans-serif" text-anchor="middle" dominant-baseline="central" paint-order="stroke" stroke="black" stroke-width="2.6" stroke-linejoin="round">')
MAIN_LABELS = [(96,246),(416,246),(268,96),(268,416),
               (200,246),(312,246),(268,200),(268,312),
               (182,134),(330,134),(330,378),(182,378),
               (92,38),(420,38),(420,474),(92,474)]
for road, (x,y) in zip(ROADS, MAIN_LABELS):
    svg.append(f'<text x="{x}" y="{y}" font-size="11" font-weight="bold">{road["id"]}</text>')
for name, sa, sb, ids in PAIRS:
    x,y = CENTERS[name]
    dx,dy = [VECTORS[sa][k]+VECTORS[sb][k] for k in (0,1)]
    svg.append(f'<text x="{x+dx*27}" y="{y+dy*24}" font-size="8.5">{ids[0]}/{ids[1]}</text>')
svg.extend(['</g>', '<text x="256" y="502" fill="white" text-anchor="middle" font-family="Arial" font-size="8">ID a/b = separate forward / return turn roads</text>', '</svg>'])
(OUT / 'road_map_v2.svg').write_text('\n'.join(svg), encoding='utf-8')

with (OUT / 'road_ids.csv').open('w', newline='', encoding='utf-8-sig') as f:
    writer = csv.DictWriter(f, fieldnames=['id','kind','junction','direction','reverseRoad','fromRoadNo','fromNode','toRoadNo','toNode','startJunction','endJunction'])
    writer.writeheader()
    for road in ROADS:
        writer.writerow({key:road.get(key,'') for key in writer.fieldnames})

# Validate endpoint references, physical reciprocity, allowed travel and reachability.
assert len(ROADS) == 72 and len(PAIRS) == 28
assert [r['id'] for r in ROADS] == list(range(1,73))
for road in ROADS:
    for node, key, default in [(0,'nodeS','defaultRoadS'), (1,'nodeE','defaultRoadE')]:
        links = road[key]
        assert len({(c['roadNo'],c['enterNode']) for c in links}) == len(links)
        assert any(c['roadNo'] == road[default] and permitted(c) for c in links)
        for c in links:
            assert 1 <= c['roadNo'] <= 72 and c['enterNode'] in (0,1)
            assert {'roadNo':road['id'],'enterNode':node} in endpoint_links(c['roadNo'],c['enterNode'])
    if road['kind'] == 'main':
        assert all(a[0] == b[0] or a[1] == b[1] for a,b in zip(road['points'],road['points'][1:]))
    else:
        partner = ROADS[road['reverseRoad']-1]
        assert partner['reverseRoad'] == road['id'] and partner['direction'] == 1-road['direction']
        assert (road['fromRoadNo'],road['fromNode']) == (partner['toRoadNo'],partner['toNode'])
        assert road['points'][0] == partner['points'][0] and road['points'][-1] == partner['points'][-1]
        for node in (0,1):
            c = endpoint_links(road['id'],node)[0]
            p = ROADS[c['roadNo']-1]['points'][0 if c['enterNode']==0 else -1]
            assert math.dist(p,road['points'][0 if node==0 else -1]) < 0.0001

for name, arms in ARMS.items():
    for source_side, source in arms.items():
        choices = [c for c in endpoint_links(*source) if permitted(c)]
        destinations = []
        for choice in choices:
            target = ROADS[choice['roadNo']-1]
            if target['kind'] == 'main':
                destinations.append((target['id'],choice['enterNode']))
            else:
                assert target['junction'] == name
                assert (target['fromRoadNo'],target['fromNode']) == source
                destinations.append((target['toRoadNo'],target['toNode']))
        assert set(destinations) == set(arms.values())-{source}
        assert len(destinations) == len(arms)-1

states = {(r['id'],node) for r in ROADS for node in r['allowedEntryNodes']}
for origin in states:
    seen = {origin}
    todo = deque([origin])
    while todo:
        rid, entry = todo.popleft()
        for c in endpoint_links(rid,1-entry):
            state = (c['roadNo'],c['enterNode'])
            if permitted(c) and state not in seen:
                seen.add(state)
                todo.append(state)
    assert seen == states, (origin,len(seen))
report = dict(roads=72,mainRoads=16,turnRoads=56,reversePairs=28,junctions=9,
              straightContinuations=sum(len(j['straightConnections']) for j in JUNCTIONS),
              directedTravelStates=len(states),allIdsValid=True,allConnectionsReciprocal=True,
              allTurnsMeetRoadEndpoints=True,noDiagonalMainRoads=True,
              exactlyOneLegalRoutePerExit=True,allTravelStatesMutuallyReachable=True)
(OUT / 'validation.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
print(json.dumps(report))
