# -*- coding: utf-8 -*-
"""Solver matricial lineal compatible con el subconjunto de OpenSeesPy usado por P1L3.

Uso: verificacion independiente cuando openseespy no esta disponible
(p. ej. Linux sin acceso a PyPI). Replica exactamente las formulaciones de
OpenSees para:

  - model('basic', '-ndm', 3, '-ndf', 6)
  - node, fix, geomTransf('Linear', tag, vx, vy, vz)
  - element('elasticBeamColumn', tag, i, j, A, E, G, J, Iy, Iz, transfTag)
  - timeSeries/pattern('Plain'), load(node, 6 comps)
  - eleLoad('-ele', *tags, '-type', '-beamUniform', Wy, Wz[, Wx])
  - analyze (estatico lineal), nodeDisp, nodeReaction, reactions,
    eleForce (global), eleResponse(tag, 'localForce' | 'globalForce'),
    getFixedNodes, wipe.

Formulacion (ElasticBeam3d + LinearCrdTransf3d de OpenSees):
  eje x local = (xj - xi)/L ; y = normalize(vecxz x x) ; z = x x y
  v (basicas) = [elong, thz_i, thz_j, thy_i, thy_j, twist]
  q = kb v + q0 ; fuerzas locales de extremo P(q, p0) iguales a 'localForce'
  eleForce = R^T P por bloques de 3 (igual a 'globalForce').

Inyeccion:  import sys, mini_opensees; sys.modules['openseespy.opensees'] = mini_opensees
"""

import numpy as np

_state = {}


def wipe():
    _state.clear()
    _state.update({
        "nodes": {}, "fix": {}, "transf": {}, "ele": {},
        "loads": {}, "eleloads": {}, "disp": None, "react": None,
        "ele_resp": {},
        "diaphragms": [],
    })


wipe()


def version():
    return "mini_opensees-1.0 (numpy)"


def model(*args):
    pass


def node(tag, x, y, z):
    _state["nodes"][int(tag)] = np.array([x, y, z], dtype=float)


def fix(tag, *dofs):
    tag = int(tag)
    cur = _state["fix"].get(tag, [0] * 6)
    _state["fix"][tag] = [max(int(a), int(b)) for a, b in zip(cur, dofs)]


def rigidDiaphragm(perp, master, *slaves):
    """Diafragma rigido en el plano perpendicular a perp (solo perp=3, plano XY):
    u_s = u_m - (y_s - y_m) rz_m ; v_s = v_m + (x_s - x_m) rz_m ; rz_s = rz_m."""
    if int(perp) != 3:
        raise NotImplementedError("solo rigidDiaphragm 3")
    _state["diaphragms"].append((int(master), [int(s) for s in slaves]))


def getFixedNodes():
    return sorted(_state["fix"])


def geomTransf(kind, tag, *vecxz):
    if kind != "Linear":
        raise NotImplementedError(kind)
    _state["transf"][int(tag)] = np.array(vecxz[:3], dtype=float)


def element(kind, tag, ni, nj, A, E, G, J, Iy, Iz, transf, *rest):
    if kind != "elasticBeamColumn":
        raise NotImplementedError(kind)
    _state["ele"][int(tag)] = dict(ni=int(ni), nj=int(nj), A=A, E=E, G=G, J=J, Iy=Iy, Iz=Iz, transf=int(transf))


def timeSeries(*a, **k):
    pass


def pattern(*a, **k):
    pass


def load(tag, *vals):
    tag = int(tag)
    cur = _state["loads"].get(tag, np.zeros(6))
    v = np.zeros(6)
    v[:len(vals)] = vals
    _state["loads"][tag] = cur + v


def eleLoad(*args):
    args = list(args)
    i = args.index("-ele")
    j = args.index("-type")
    tags = [int(t) for t in args[i + 1:j]]
    typ = args[j + 1]
    vals = [float(v) for v in args[j + 2:]]
    if typ != "-beamUniform":
        raise NotImplementedError(typ)
    wy, wz = vals[0], vals[1]
    wx = vals[2] if len(vals) > 2 else 0.0
    for t in tags:
        cur = _state["eleloads"].get(t, np.zeros(3))
        _state["eleloads"][t] = cur + np.array([wy, wz, wx])


# -- no-ops de configuracion del analisis --------------------------------
def system(*a): pass
def numberer(*a): pass
def constraints(*a): pass
def integrator(*a): pass
def algorithm(*a): pass
def analysis(*a): pass


def _frame(e):
    xi = _state["nodes"][e["ni"]]
    xj = _state["nodes"][e["nj"]]
    d = xj - xi
    L = float(np.linalg.norm(d))
    ex = d / L
    vxz = _state["transf"][e["transf"]]
    ey = np.cross(vxz, ex)
    ey /= np.linalg.norm(ey)
    ez = np.cross(ex, ey)
    R = np.vstack([ex, ey, ez])  # filas = ejes locales en coords globales
    return L, R


def _basic(e, L):
    # matriz A: v = A ul  (ul = 12 desplazamientos locales)
    A = np.zeros((6, 12))
    A[0, 0], A[0, 6] = -1.0, 1.0
    A[1, 1], A[1, 7], A[1, 5] = 1.0 / L, -1.0 / L, 1.0
    A[2, 1], A[2, 7], A[2, 11] = 1.0 / L, -1.0 / L, 1.0
    A[3, 2], A[3, 8], A[3, 4] = -1.0 / L, 1.0 / L, 1.0
    A[4, 2], A[4, 8], A[4, 10] = -1.0 / L, 1.0 / L, 1.0
    A[5, 3], A[5, 9] = -1.0, 1.0
    E, G = e["E"], e["G"]
    kb = np.zeros((6, 6))
    kb[0, 0] = E * e["A"] / L
    EIz, EIy = E * e["Iz"], E * e["Iy"]
    kb[1, 1] = kb[2, 2] = 4 * EIz / L
    kb[1, 2] = kb[2, 1] = 2 * EIz / L
    kb[3, 3] = kb[4, 4] = 4 * EIy / L
    kb[3, 4] = kb[4, 3] = 2 * EIy / L
    kb[5, 5] = G * e["J"] / L
    return A, kb


def _fixed_end(tag, L):
    p0 = np.zeros(5)
    q0 = np.zeros(6)
    w = _state["eleloads"].get(tag)
    if w is None:
        return p0, q0
    wy, wz, wx = w
    Vy = 0.5 * wy * L
    Mz = Vy * L / 6.0
    Vz = 0.5 * wz * L
    My = Vz * L / 6.0
    P = wx * L
    p0[0] -= P
    p0[1] -= Vy
    p0[2] -= Vy
    p0[3] -= Vz
    p0[4] -= Vz
    q0[0] -= 0.5 * P
    q0[1] -= Mz
    q0[2] += Mz
    q0[3] += My
    q0[4] -= My
    return p0, q0


def _local_from_basic(q, p0, L):
    P = np.zeros(12)
    P[6] = q[0]
    P[0] = -q[0] + p0[0]
    P[9] = q[5]
    P[3] = -q[5]
    P[5] = q[1]
    P[11] = q[2]
    V = (q[1] + q[2]) / L
    P[1] = V + p0[1]
    P[7] = -V + p0[2]
    P[4] = q[3]
    P[10] = q[4]
    V = (q[3] + q[4]) / L
    P[2] = -V + p0[3]
    P[8] = V + p0[4]
    return P


def analyze(n=1):
    nodes = sorted(_state["nodes"])
    idx = {t: k for k, t in enumerate(nodes)}
    ndof = 6 * len(nodes)
    K = np.zeros((ndof, ndof))
    F = np.zeros(ndof)
    cache = {}
    for tag, e in _state["ele"].items():
        L, R = _frame(e)
        A, kb = _basic(e, L)
        T = np.zeros((12, 12))
        for b in range(4):
            T[3 * b:3 * b + 3, 3 * b:3 * b + 3] = R
        kl = A.T @ kb @ A
        kg = T.T @ kl @ T
        p0, q0 = _fixed_end(tag, L)
        pl0 = _local_from_basic(q0, p0, L)  # fuerzas de extremo con u=0
        pg0 = T.T @ pl0
        dofs = np.r_[6 * idx[e["ni"]]:6 * idx[e["ni"]] + 6, 6 * idx[e["nj"]]:6 * idx[e["nj"]] + 6]
        K[np.ix_(dofs, dofs)] += kg
        F[dofs] -= pg0
        cache[tag] = (L, T, A, kb, p0, q0, dofs)
    for t, v in _state["loads"].items():
        F[6 * idx[t]:6 * idx[t] + 6] += v
    fixed = np.zeros(ndof, dtype=bool)
    for t, f in _state["fix"].items():
        for k in range(6):
            if f[k]:
                fixed[6 * idx[t] + k] = True
    # Restricciones multipunto (Transformation): u = T q
    dependent = {}
    for master, slaves in _state["diaphragms"]:
        xm = _state["nodes"][master]
        dm = 6 * idx[master]
        for s_ in slaves:
            xs = _state["nodes"][s_]
            ds = 6 * idx[s_]
            dependent[ds + 0] = [(dm + 0, 1.0), (dm + 5, -(xs[1] - xm[1]))]
            dependent[ds + 1] = [(dm + 1, 1.0), (dm + 5, (xs[0] - xm[0]))]
            dependent[ds + 5] = [(dm + 5, 1.0)]
    retained = [k for k in range(ndof) if not fixed[k] and k not in dependent]
    col = {k: c for c, k in enumerate(retained)}
    T = np.zeros((ndof, len(retained)))
    for k in retained:
        T[k, col[k]] = 1.0
    for k, terms in dependent.items():
        for m, c in terms:
            if m in col:
                T[k, col[m]] += c
    Kr = T.T @ K @ T
    Fr = T.T @ F
    u = np.zeros(ndof)
    try:
        q = np.linalg.solve(Kr, Fr)
        for _ in range(2):  # refinamiento iterativo (muros/brazos muy rigidos)
            q = q - np.linalg.solve(Kr, Kr @ q - Fr)
    except np.linalg.LinAlgError:
        return -3
    u = T @ q
    resid = Kr @ q - Fr
    scale = max(1.0, float(np.abs(Fr).max()))
    if not np.all(np.isfinite(u)) or np.abs(resid).max() > 1e-6 * scale:
        return -3
    # fuerzas de elemento y reacciones
    Rint = np.zeros(ndof)
    resp = {}
    for tag, (L, T, A, kb, p0, q0, dofs) in cache.items():
        ul = T @ u[dofs]
        q = kb @ (A @ ul) + q0
        pl = _local_from_basic(q, p0, L)
        pg = T.T @ pl
        resp[tag] = (pl, pg)
        Rint[dofs] += pg
    applied = np.zeros(ndof)
    for t, v in _state["loads"].items():
        applied[6 * idx[t]:6 * idx[t] + 6] += v
    react = Rint - applied
    react[~fixed] = 0.0  # solo apoyos (las fuerzas de restriccion del diafragma no son reacciones)
    _state["disp"] = {t: u[6 * idx[t]:6 * idx[t] + 6].copy() for t in nodes}
    _state["react"] = {t: react[6 * idx[t]:6 * idx[t] + 6].copy() for t in nodes}
    _state["ele_resp"] = resp
    _state["cond_residual"] = float(np.abs(resid).max())
    return 0


def reactions(*a):
    pass


def nodeDisp(tag, dof=None):
    v = _state["disp"][int(tag)]
    return float(v[dof - 1]) if dof else [float(x) for x in v]


def nodeReaction(tag, dof=None):
    v = _state["react"][int(tag)]
    return float(v[dof - 1]) if dof else [float(x) for x in v]


def eleForce(tag, dof=None):
    v = _state["ele_resp"][int(tag)][1]
    return float(v[dof - 1]) if dof else [float(x) for x in v]


def eleResponse(tag, *args):
    what = args[0] if args else "force"
    pl, pg = _state["ele_resp"][int(tag)]
    if what in ("localForce", "localForces"):
        return [float(x) for x in pl]
    if what in ("force", "forces", "globalForce", "globalForces"):
        return [float(x) for x in pg]
    raise NotImplementedError(what)


def _unsupported(*a, **k):
    raise NotImplementedError("mini_opensees solo implementa el analisis de porticos elasticos")


uniaxialMaterial = section = patch = layer = _unsupported
