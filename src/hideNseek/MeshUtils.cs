using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StereoKit;

namespace hideNseek
{
	class MeshUtils
	{
		static public Mesh createArrow(float dx, float dy, float dz)
		{
			Vertex[] verts = new Vertex[5];
			verts[0] = new Vertex(new Vec3(0, 0, dz), new Vec3(0, 0, dz));
			verts[1] = new Vertex(new Vec3(0, dy, 0), new Vec3(0, dy, 0));
			verts[2] = new Vertex(new Vec3(dx, 0, 0), new Vec3(dx, 0, 0));
			verts[3] = new Vertex(new Vec3(0, -dy, 0), new Vec3(0, -dy, 0));
			verts[4] = new Vertex(new Vec3(-dx, 0, 0), new Vec3(-dx, 0, 0));

			uint[] inds = new uint[] { 0, 2, 1, 0, 1, 4, 0, 4, 3, 0, 3, 2, 4, 1, 2, 4, 2, 3 };

			Mesh m = new Mesh();
			m.SetVerts(verts);
			m.SetInds(inds);
			return m;
		}
	}
}