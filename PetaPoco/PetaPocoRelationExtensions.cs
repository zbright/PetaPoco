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
        public static List<T> FetchOneToMany<T, T1>(this IDatabase db, Func<T, object> key, Expression<Func<T, List<T1>>> list, Sql Sql)
        {
            return db.FetchOneToMany(key, list, Sql.SQL, Sql.Arguments);
        }

        public static List<T> FetchManyToOne<T, T1>(this IDatabase db, Func<T, object> key, Expression<Func<T, T1>> item, Sql Sql)
        {
            return db.FetchManyToOne(key, item, Sql.SQL, Sql.Arguments);
        }

        public static List<T> FetchManyToOne<T, T1, T2>(this IDatabase db, Func<T, object> key, Expression<Func<T, T1>> item, Expression<Func<T, T2>> item2, Sql Sql)
        {
            return db.FetchManyToOne(key, item, item2, Sql.SQL, Sql.Arguments);
        }

        public static List<T> FetchOneToMany<T, T1>(this IDatabase db, Func<T, object> key, Expression<Func<T, List<T1>>> list, string sql, params object[] args)
        {
            var relator = new Relator();
            return db.Fetch<T, T1, T>((a, b) => relator.OneToMany(a, b, key, list), sql, args);
        }

        public static List<T> FetchManyToOne<T, T1>(this IDatabase db, Func<T, object> key, Expression<Func<T, T1>> item, string sql, params object[] args)
        {
            var relator = new Relator();
            return db.Fetch<T, T1, T>((a, b) => relator.ManyToOne(a, b, key, item), sql, args);
        }

        public static List<T> FetchManyToOne<T, T1, T2>(this IDatabase db, Func<T, object> key, Expression<Func<T, T1>> item, Expression<Func<T, T2>> item2, string sql, params object[] args)
        {
            var relator = new Relator();
            return db.Fetch<T, T1, T2, T>((a, b, c) => relator.ManyToOne(a, b, c, key, item, item2), sql, args);
        }
    }

    public class Relator
    {
        private Dictionary<object, object> onemanytoone = new Dictionary<object, object>();
        public T ManyToOne<T, U>(T main, U sub, Func<T, object> idFunc, Expression<Func<T, U>> item)
        {
            object aExisting;
            if (onemanytoone.TryGetValue(idFunc(main), out aExisting))
                sub = (U)aExisting;
            else
                onemanytoone.Add(idFunc(main), sub);

            // Wire up objects
            AssignValue(item, main, sub);

            return main;
        }

        private Dictionary<string, object> twomanytoone = new Dictionary<string, object>();
        public T ManyToOne<T, TSub1, TSub2>(T main, TSub1 sub1, TSub2 sub2, Func<T, object> idFunc, Expression<Func<T, TSub1>> item, Expression<Func<T, TSub2>> item2)
        {
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

            // Wire up objects
            AssignValue(item, main, sub1);
            AssignValue(item2, main, sub2);

            return main;
        }

        private object onetomanycurrent;
        public T OneToMany<T, TSub>(T main, TSub sub, Func<T, object> idFunc, Expression<Func<T, List<TSub>>> item)
        {
            if (main == null)
                return (T)onetomanycurrent;

            if (onetomanycurrent != null && idFunc((T)onetomanycurrent) == idFunc(main))
            {
                item.Compile()((T)onetomanycurrent).Add(sub);
                return default(T);
            }

            var prev = (T)onetomanycurrent;
            onetomanycurrent = main;
            AssignValue(item, (T)onetomanycurrent, new List<TSub> { sub });

            return prev;
        }

        private static void AssignValue<TSource, TResult>(Expression<Func<TSource, TResult>> expression, TSource source, TResult result)
        {
            var paramExp = expression.Parameters.Single();
            Expression assignExp;
#if PETAPOCO_NO_DYNAMIC    
            assignExp = AssignmentExpression.Create(expression.Body, Expression.Constant(result));
#else
            assignExp = Expression.Assign(expression.Body, Expression.Constant(result));
#endif
            
            var lambdaExp = Expression.Lambda(assignExp, paramExp);
            lambdaExp.Compile().DynamicInvoke(source);
        }

        private static class AssignmentExpression
        {
            public static Expression Create(Expression left, Expression right)
            {
                return
                    Expression.Call(
                       null,
                       typeof(AssignmentExpression)
                          .GetMethod("AssignTo", BindingFlags.NonPublic | BindingFlags.Static)
                          .MakeGenericMethod(left.Type),
                       left,
                       right);
            }

            private static void AssignTo<T>(ref T left, T right)  // note the 'ref', which is
            {                                                     // important when assigning
                left = right;                                     // to value types!
            }
        }
    }
}
