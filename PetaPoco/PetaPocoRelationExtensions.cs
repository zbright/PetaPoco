using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace PetaPoco
{
    public static class PetaPocoRelationExtensions
    {
        public static List<T> FetchOneToMany<T, T1>(this IDatabase db, Func<T, object> key, Sql Sql)
        {
            return db.FetchOneToMany<T, T1>(key, Sql.SQL, Sql.Arguments);
        }

        public static List<T> FetchManyToOne<T, T1>(this IDatabase db, Func<T, object> key, Sql Sql)
        {
            return db.FetchManyToOne<T, T1>(key, Sql.SQL, Sql.Arguments);
        }

        public static List<T> FetchManyToOne<T, T1, T2>(this IDatabase db, Func<T, object> key, Sql Sql)
        {
            return db.FetchManyToOne<T, T1, T2>(key, Sql.SQL, Sql.Arguments);
        }

        public static List<T> FetchOneToMany<T, T1>(this IDatabase db, Func<T, object> key, string sql, params object[] args)
        {
            var relator = new Relator();
            return db.Fetch<T, T1, T>((a, b) => relator.OneToMany(a, b, key), sql, args);
        }

        public static List<T> FetchManyToOne<T, T1>(this IDatabase db, Func<T, object> key, string sql, params object[] args)
        {
            var relator = new Relator();
            return db.Fetch<T, T1, T>((a, b) => relator.ManyToOne(a, b, key), sql, args);
        }

        public static List<T> FetchManyToOne<T, T1, T2>(this IDatabase db, Func<T, object> key, string sql, params object[] args)
        {
            var relator = new Relator();
            return db.Fetch<T, T1, T2, T>((a, b, c) => relator.ManyToOne(a, b, c, key), sql, args);
        }
    }

    public class Relator
    {
        private Dictionary<object, object> onemanytoone = new Dictionary<object, object>();
        public T ManyToOne<T, TSub1>(T main, TSub1 sub, Func<T, object> idFunc)
        {
            if (property1 == null)
            {
                property1 = typeof(T).GetProperties().Where(x => typeof(TSub1) == x.PropertyType).FirstOrDefault();
                if (property1 == null)
                    ThrowPropertyNotFoundException<T, TSub1>();
            }

            object aExisting;
            if (onemanytoone.TryGetValue(idFunc(main), out aExisting))
                sub = (TSub1)aExisting;
            else
                onemanytoone.Add(idFunc(main), sub);

            property1.SetValue(main, sub, null);

            return main;
        }

        private Dictionary<string, object> twomanytoone = new Dictionary<string, object>();
        public T ManyToOne<T, TSub1, TSub2>(T main, TSub1 sub1, TSub2 sub2, Func<T, object> idFunc)
        {
            if (property1 == null)
            {
                property1 = typeof(T).GetProperties().Where(x => typeof(TSub1) == x.PropertyType).FirstOrDefault();
                if (property1 == null)
                    ThrowPropertyNotFoundException<T, TSub1>();
            }

            if (property2 == null)
            {
                property2 = typeof(T).GetProperties().Where(x => typeof(TSub2) == x.PropertyType).FirstOrDefault();
                if (property2 == null)
                    ThrowPropertyNotFoundException<T, TSub2>();
            }

            object aExisting;
            if (twomanytoone.TryGetValue(idFunc(main) + typeof(TSub1).Name, out aExisting))
                sub1 = (TSub1)aExisting;
            else
                twomanytoone.Add(idFunc(main) + typeof(TSub1).Name, sub1);

            object aExisting2;
            if (twomanytoone.TryGetValue(idFunc(main) + typeof(TSub2).Name, out aExisting2))
                sub2 = (TSub2)aExisting2;
            else
                twomanytoone.Add(idFunc(main) + typeof(TSub2).Name, sub2);

            property1.SetValue(main, sub1, null);
            property2.SetValue(main, sub2, null);

            return main;
        }

        private object onetomanycurrent;
        private PropertyInfo property1;
        private PropertyInfo property2;
        public T OneToMany<T, TSub>(T main, TSub sub, Func<T, object> idFunc)
        {
            if (main == null)
                return (T)onetomanycurrent;

            if (property1 == null)
            {
                property1 = typeof(T).GetProperties().Where(x => typeof(ICollection<TSub>).IsAssignableFrom(x.PropertyType)).FirstOrDefault();
                if (property1 == null)
                    ThrowPropertyNotFoundException<T, ICollection<TSub>>();
            }

            if (onetomanycurrent != null && idFunc((T)onetomanycurrent).Equals(idFunc(main)))
            {
                ((ICollection<TSub>)property1.GetValue((T)onetomanycurrent, null)).Add(sub);
                return default(T);
            }

            var prev = (T)onetomanycurrent;
            onetomanycurrent = main;

            property1.SetValue((T)onetomanycurrent, new List<TSub> { sub }, null);

            return prev;
        }

        private static void ThrowPropertyNotFoundException<T, TSub1>()
        {
            throw new Exception(string.Format("No Property of type {0} found on object of type: {1}", typeof(TSub1).Name, typeof(T).Name));
        }
    }
}
