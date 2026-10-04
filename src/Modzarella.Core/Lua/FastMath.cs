using System;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Interop;
using UnityEngine;

namespace Modz
{
    // MoonSharp finds operators by reflection; vectors get a fast paths.
    class FastVector3 : IUserDataDescriptor
    {
        readonly IUserDataDescriptor inner = new StandardUserDataDescriptor(typeof(Vector3), InteropAccessMode.Default);

        static Vector3 V(DynValue d) => (Vector3)d.UserData.Object;
        static DynValue New(Vector3 v) => UserData.Create(v);
        static float N(DynValue d) => (float)d.Number;

        static readonly DynValue add = DynValue.NewCallback((c, a) => New(V(a[0]) + V(a[1])));
        static readonly DynValue sub = DynValue.NewCallback((c, a) => New(V(a[0]) - V(a[1])));
        static readonly DynValue unm = DynValue.NewCallback((c, a) => New(-V(a[0])));
        static readonly DynValue div = DynValue.NewCallback((c, a) => New(V(a[0]) / N(a[1])));
        static readonly DynValue eq = DynValue.NewCallback((c, a) => DynValue.NewBoolean(a[0].Type == DataType.UserData && a[1].Type == DataType.UserData && V(a[0]) == V(a[1])));
        static readonly DynValue mul = DynValue.NewCallback((c, a) =>
            a[0].Type == DataType.Number ? New(V(a[1]) * N(a[0])) : a[1].Type == DataType.Number ? New(V(a[0]) * N(a[1])) : New(Vector3.Scale(V(a[0]), V(a[1]))));

        public string Name => inner.Name;
        public Type Type => typeof(Vector3);
        public string AsString(object obj) => obj.ToString();
        public bool IsTypeCompatible(Type type, object obj) => inner.IsTypeCompatible(type, obj);
        public bool SetIndex(Script script, object obj, DynValue index, DynValue value, bool isDirectIndexing) => inner.SetIndex(script, obj, index, value, isDirectIndexing);

        public DynValue Index(Script script, object obj, DynValue index, bool isDirectIndexing)
        {
            if (obj is Vector3 v && index.Type == DataType.String)
                switch (index.String)
                {
                    case "x": return DynValue.NewNumber(v.x);
                    case "y": return DynValue.NewNumber(v.y);
                    case "z": return DynValue.NewNumber(v.z);
                    case "magnitude": return DynValue.NewNumber(v.magnitude);
                    case "sqrMagnitude": return DynValue.NewNumber(v.sqrMagnitude);
                    case "normalized": return New(v.normalized);
                }
            return inner.Index(script, obj, index, isDirectIndexing);
        }

        public DynValue MetaIndex(Script script, object obj, string metaname)
        {
            switch (metaname)
            {
                case "__add": return add;
                case "__sub": return sub;
                case "__mul": return mul;
                case "__div": return div;
                case "__unm": return unm;
                case "__eq": return eq;
                default: return inner.MetaIndex(script, obj, metaname);
            }
        }
    }

    class FastQuaternion : IUserDataDescriptor
    {
        readonly IUserDataDescriptor inner = new StandardUserDataDescriptor(typeof(Quaternion), InteropAccessMode.Default);

        static readonly DynValue mul = DynValue.NewCallback((c, a) =>
        {
            var q = (Quaternion)a[0].UserData.Object;
            var rhs = a[1].UserData.Object;
            return rhs is Vector3 v ? UserData.Create(q * v) : UserData.Create(q * (Quaternion)rhs);
        });

        public string Name => inner.Name;
        public Type Type => typeof(Quaternion);
        public string AsString(object obj) => obj.ToString();
        public bool IsTypeCompatible(Type type, object obj) => inner.IsTypeCompatible(type, obj);
        public bool SetIndex(Script script, object obj, DynValue index, DynValue value, bool isDirectIndexing) => inner.SetIndex(script, obj, index, value, isDirectIndexing);
        public DynValue Index(Script script, object obj, DynValue index, bool isDirectIndexing) => inner.Index(script, obj, index, isDirectIndexing);
        public DynValue MetaIndex(Script script, object obj, string metaname) => metaname == "__mul" ? mul : inner.MetaIndex(script, obj, metaname);
    }
}
