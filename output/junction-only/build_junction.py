"""Single junction: 4 main roads and 4 shared, bidirectional turns."""
from pathlib import Path
import json
import math

OUT = Path(__file__).resolve().parent
YELLOW, GREEN = '#FFE600', '#39FF14'
PORTS = {1: [256,348], 2: [256,164], 3: [164,256], 4: [348,256]}
ENDS = {1: 1, 2: 0, 3: 1, 4: 0}
POINTS = {1:[[256,456],PORTS[1]], 2:[PORTS[2],[256,56]],
          3:[[56,256],PORTS[3]], 4:[PORTS[4],[456,256]]}
ROADS = []
for rid in range(1,5):
    ROADS.append(dict(id=rid,label=str(rid),kind='main',color='#FFFFFF',bidirectional=True,
                      nodeS=[],nodeE=[],
                      defaultRoadS=0,defaultRoadE=0,points=POINTS[rid]))


def links(rid, node):
    return ROADS[rid-1]['nodeS' if node == 0 else 'nodeE']


def connect(a, b):
    links(*a).append(dict(roadNo=b[0],enterNode=b[1]))
    links(*b).append(dict(roadNo=a[0],enterNode=a[1]))


connect((1,1),(2,0))
connect((3,1),(4,0))
ROADS[0]['defaultRoadE'] = 2
ROADS[1]['defaultRoadS'] = 1
ROADS[2]['defaultRoadE'] = 4
ROADS[3]['defaultRoadS'] = 3

PAIRS = [(1,3),(1,4),(3,2),(4,2)]
for source, target in PAIRS:
    p,q = PORTS[source],PORTS[target]
    rid = len(ROADS)+1
    control = [256,256]
    points = []
    for i in range(33):
        t = i/32
        points.append([round((1-t)**2*p[k]+2*(1-t)*t*control[k]+t*t*q[k],4) for k in (0,1)])
    label = '-'.join(map(str,sorted((source,target))))
    ROADS.append(dict(id=rid,label=label,kind='turn',color='#FFFFFF',bidirectional=True,
                      directionColors={'S_to_E':YELLOW,'E_to_S':GREEN},
                      nodeS=[],nodeE=[],defaultRoadS=source,defaultRoadE=target,
                      controlPoint=control,points=points))
    connect((source,ENDS[source]),(rid,0))
    connect((target,ENDS[target]),(rid,1))

data = dict(
    schemaVersion=3,
    nodeConvention={'0':'S / first point','1':'E / last point'},
    travelDirectionByEnterNode={'0':'S to E; follow points in order','1':'E to S; follow points in reverse'},
    notes=[
        'Main roads: 1=south, 2=north, 3=west, 4=east.',
        'One bidirectional road ID per turn pair: 5=1-3, 6=1-4, 7=2-3, 8=2-4.',
        'Yellow and green show opposite travel directions on the SAME geometry and ID; they are not separate roads.',
        'All roads allow entry at S or E. Select movement direction from enterNode; exit at the opposite endpoint.',
        'Straight travel 1<->2 and 3<->4 connects directly without an extra road ID.',
        'The four outer endpoints are open boundaries: empty nodeS/nodeE and defaultRoad 0 mean no next road.',
        'Coordinates use a 512x512 canvas with y increasing downward. Interpolate straightConnections to cross the space between arm endpoints.',
    ],
    roads=ROADS,
    straightConnections=[
        dict(fromRoadNo=1,fromNode=1,toRoadNo=2,toNode=0,bidirectional=True,points=[PORTS[1],PORTS[2]]),
        dict(fromRoadNo=3,fromNode=1,toRoadNo=4,toNode=0,bidirectional=True,points=[PORTS[3],PORTS[4]]),
    ])
(OUT/'junction.json').write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')

# A compact copy without geometry is useful when editing only connectivity.
compact = {k:v for k,v in data.items() if k!='straightConnections'}
compact['roads'] = [{k:v for k,v in r.items() if k not in ('points','controlPoint')} for r in ROADS]
compact['notes'] = [n for n in compact['notes'] if not n.startswith('Coordinates')]
(OUT/'junction_connections.json').write_text(json.dumps(compact,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')

svg = ['<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512" viewBox="0 0 512 512">',
       '<rect width="512" height="512" fill="#000"/>',
       '<path d="M256 56 V456 M56 256 H456" fill="none" stroke="white" stroke-width="2.5"/>']
for r in ROADS[4:]:
    points = ' '.join(f'{x},{y}' for x,y in r['points'])
    svg.append(f'<polyline points="{points}" fill="none" stroke="{r["color"]}" stroke-width="3" stroke-linecap="round"/>')
    # Opposite arrows occupy the same curve; no duplicate return geometry.
    for index,sign,color in [(11,1,YELLOW),(21,-1,GREEN)]:
        mid = r['points'][index]
        a,b = r['points'][index-1],r['points'][index+1]
        dx,dy = b[0]-a[0],b[1]-a[1]
        length = math.hypot(dx,dy)
        ux,uy = sign*dx/length,sign*dy/length
        tip = [mid[0]+ux*7,mid[1]+uy*7]
        left = [mid[0]-ux*5-uy*4,mid[1]-uy*5+ux*4]
        right = [mid[0]-ux*5+uy*4,mid[1]-uy*5-ux*4]
        triangle = ' '.join(f'{x},{y}' for x,y in [tip,left,right])
        svg.append(f'<polygon points="{triangle}" fill="{color}"/>')
svg.append('<g font-family="Arial, sans-serif" text-anchor="middle" dominant-baseline="central">')
for rid,x,y in [(1,256,479),(2,256,32),(3,33,256),(4,479,256)]:
    svg.append(f'<text x="{x}" y="{y}" fill="white" font-size="25" font-weight="bold">{rid}</text>')
positions = [(164,334),(348,334),(164,178),(348,178)]
for road,(x,y) in zip(ROADS[4:],positions):
    label = road['label']
    svg.append(f'<text x="{x}" y="{y}" fill="white" font-size="21">{label}</text>')
svg.append('</g></svg>')
(OUT/'junction.svg').write_text('\n'.join(svg),encoding='utf-8')

# Verify exact user-specified turns and every legal junction exit.
assert len(ROADS)==8
assert [r['id'] for r in ROADS]==list(range(1,9))
assert [r['label'] for r in ROADS[4:]]==['1-3','1-4','2-3','2-4']
for road in ROADS:
    assert road['bidirectional']
    assert not any(key in road for key in ('reverseRoad','direction','allowedEntryNodes','fromRoadNo','toRoadNo'))
    for node,key,default in [(0,'nodeS','defaultRoadS'),(1,'nodeE','defaultRoadE')]:
        assert len(road[key])==len({(c['roadNo'],c['enterNode']) for c in road[key]})
        for c in road[key]:
            assert {'roadNo':road['id'],'enterNode':node} in links(c['roadNo'],c['enterNode'])
        assert (road[default]==0 and not road[key]) or any(c['roadNo']==road[default] for c in road[key])
    if road['kind']=='turn':
        for node in (0,1):
            c=links(road['id'],node)[0]
            endpoint=ROADS[c['roadNo']-1]['points'][0 if c['enterNode']==0 else -1]
            assert endpoint==road['points'][0 if node==0 else -1]
for rid in range(1,5):
    destinations=[]
    for c in links(rid,ENDS[rid]):
        target=ROADS[c['roadNo']-1]
        if target['kind']=='turn':
            entry=c['enterNode']
            assert links(target['id'],entry)==[dict(roadNo=rid,enterNode=ENDS[rid])]
            destinations.append(links(target['id'],1-entry)[0]['roadNo'])
        else:
            destinations.append(target['id'])
    assert set(destinations)=={1,2,3,4}-{rid} and len(destinations)==3
assert len({tuple(sorted((r['nodeS'][0]['roadNo'],r['nodeE'][0]['roadNo']))) for r in ROADS[4:]})==4
assert len([r for r in ROADS if r['kind']=='turn'])==4
print('PASS: 8 bidirectional roads; 4 unique shared turns; reciprocal endpoints; 3 exits per arm; no separate return IDs.')
