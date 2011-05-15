using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using PetaPoco.FluentConfiguration;

namespace PetaPoco.Tests
{
    [TestFixture]
    public class FluentMappingTests
    {
        [Test]
        public void TypeIsCorrectlySet()
        {
            var map = new MyMap() as IPetaPocoMap;
            var def = map.TypeDefinition;
            Assert.AreEqual(def.Type, typeof(fluentpoco));
        }

        [Test]
        public void TableNameIsCorrectlyMapped()
        {
            var map = new MyMap() as IPetaPocoMap;
            var def = map.TypeDefinition;
            Assert.AreEqual(def.TableName, "fluentpoco");
        }

        [Test]
        public void SequenceIsCorrectlyMapped()
        {
            var map = new MyMap() as IPetaPocoMap;
            var def = map.TypeDefinition;
            Assert.AreEqual(def.SequenceName, "fluentseq");
        }

        [Test]
        public void PrimaryKeyIsCorrectlyMapped()
        {
            var map = new MyMap() as IPetaPocoMap;
            var def = map.TypeDefinition;
            Assert.AreEqual(def.PrimaryKey, "Id");
            Assert.AreEqual(def.AutoIncrement, true);
        }

        [Test]
        public void ColumnCorrectlyMapped()
        {
            var map = new MyMap() as IPetaPocoMap;
            var def = map.TypeDefinition;
            Assert.AreEqual(def.ColumnConfiguration["Id"].DbColumnName, "myId");
        }

        [Test]
        public void IgnoreCorrectlyMapped()
        {
            var map = new MyMap() as IPetaPocoMap;
            var def = map.TypeDefinition;
            Assert.AreEqual(def.ColumnConfiguration["Name"].IgnoreColumn, true);
        }

        [Test]
        public void ResultCorrectlyMapped()
        {
            var map = new MyMap() as IPetaPocoMap;
            var def = map.TypeDefinition;
            Assert.AreEqual(def.ColumnConfiguration["Result"].ResultColumn, true);
            Assert.AreEqual(def.ColumnConfiguration["Result"].DbColumnName, "fluentresult");
        }

        [Test]
        public void VersionCorrectlyMapped()
        {
            var map = new MyMap() as IPetaPocoMap;
            var def = map.TypeDefinition;
            Assert.AreEqual(def.ColumnConfiguration["Ver"].VersionColumn, true);
            Assert.AreEqual(def.ColumnConfiguration["Ver"].DbColumnName, "fluentversion");
        }

        [Test]
        public void CompositeKeyCorrectlyMapped()
        {
            var map = new MyMapComp() as IPetaPocoMap;
            var def = map.TypeDefinition;
            Assert.AreEqual(def.PrimaryKey, "Id,Name");
        }

        [Test]
        public void ExplicitColumnsCorrectlySetToFalse()
        {
            var map = new MyMapComp() as IPetaPocoMap;
            var def = map.TypeDefinition;
            Assert.AreEqual(def.ExplicitColumns, false);
        }

        [Test]
        public void ExplicitColumnsCorrectlySetToTrue()
        {
            var map = new MyMap() as IPetaPocoMap;
            var def = map.TypeDefinition;
            Assert.AreEqual(def.ExplicitColumns, true);
        }

    }

    public class fluentpoco
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Result { get; set; }
        public int Ver { get; set; }
    }

    public class MyMap : PetaPocoMap<fluentpoco>
    {
        public MyMap()
        {
            PrimaryKey(x => x.Id, "fluentseq");
            TableName("fluentpoco");
            Columns(x =>
                        {
                            x.Column(y => y.Id, "myId");
                            x.Ignore(y => y.Name);
                            x.Result(y => y.Result, "fluentresult");
                            x.Version(y => y.Ver, "fluentversion");
                        }, true);
        }
    }

    public class fluentpococomp
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public class MyMapComp : PetaPocoMap<fluentpococomp>
    {
        public MyMapComp()
        {
            CompositePrimaryKey(x => x.Id, x => x.Name);
        }
    }

}
