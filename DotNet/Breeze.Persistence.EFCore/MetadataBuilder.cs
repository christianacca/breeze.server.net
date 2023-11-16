using Breeze.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;

namespace Breeze.Persistence.EFCore {


  public class MetadataBuilder {

    public static BreezeMetadata BuildFrom(DbContext dbContext) {
      return new MetadataBuilder().GetMetadataFromContext(dbContext);
    }

    private BreezeMetadata GetMetadataFromContext(DbContext dbContext) {
      var metadata = new BreezeMetadata();
      var dbSetMap = GetDbSetMap(dbContext);
      metadata.StructuralTypes = dbContext.Model.GetEntityTypes()
        .Where(et => !et.IsOwned())
        .Select(et => CreateMetaType(et, dbSetMap)).ToList();


      // Complex types show up once per parent reference and we need to reduce
      // this to just the unique types.
      var complexTypes = dbContext.Model.GetEntityTypes()
        .Where(et => et.IsOwned())
        .Select(et => CreateMetaType(et, dbSetMap))
#if NET6_0_OR_GREATER
        .DistinctBy(et => et.ShortName).ToList();
#else
        .Distinct(new MetaTypeEqualityComparer()).ToList();
#endif
      complexTypes.ForEach(v => metadata.StructuralTypes.Insert(0, v));

      // Get the enums out of the model types
      var enums = dbContext.Model.GetEntityTypes()
      .SelectMany(et => et.GetProperties().Where(p => p.PropertyInfo != null && IsEnum(p.PropertyInfo.PropertyType))).ToList();

      if (enums.Any()) {
        metadata.EnumTypes = new List<MetaEnum>();

        foreach (var myEnum in enums) {
          var realType = myEnum.ClrType;
          // Check if realType is nullable
          if (Nullable.GetUnderlyingType(realType) != null) {
            realType = Nullable.GetUnderlyingType(realType);
          }

          string[] enumNames = Enum.GetNames(realType);
          int[] enumOrds = Enum.GetValues(realType) as int[];
          var et = new MetaEnum {
            ShortName = realType.Name,
            Namespace = realType.Namespace,
            Values = enumNames,
            Ordinals = enumOrds
          };
          if (!metadata.EnumTypes.Exists(x => x.ShortName == realType.Name)) {
            metadata.EnumTypes.Add(et);
          }
        }
      }

      return metadata;
    }

    private static bool IsEnum(Type type) {
      return type.IsEnum || (Nullable.GetUnderlyingType(type) != null && Nullable.GetUnderlyingType(type).IsEnum);
    }

    private static Dictionary<Type, String> GetDbSetMap(DbContext context) {
      var dbSetProperties = new List<PropertyInfo>();
      var properties = context.GetType().GetProperties();
      var result = new Dictionary<Type, String>();
      foreach (var property in properties) {
        var setType = property.PropertyType;
        var isDbSet = setType.IsGenericType && (typeof(DbSet<>).IsAssignableFrom(setType.GetGenericTypeDefinition()));
        if (isDbSet) {
          var entityType = setType.GetGenericArguments()[0];
          var resourceName = property.Name;
          result.Add(entityType, resourceName);
        }
      }
      return result;
    }

    private MetaType CreateMetaType(Microsoft.EntityFrameworkCore.Metadata.IEntityType et, Dictionary<Type, String> dbSetMap) {
      var mt = new MetaType {
        ShortName = et.ClrType.Name,
        Namespace = et.ClrType.Namespace
      };
      if (et.IsOwned()) {
        mt.IsComplexType = true;
      }
      if (dbSetMap.TryGetValue(et.ClrType, out string resourceName)) {
        mt.DefaultResourceName = resourceName;
      }
      var baseType = et.BaseType;
      if (baseType != null) {
        mt.BaseTypeName = baseType.ClrType.Name + ":#" + baseType.ClrType.Namespace;
      }
      if (et.IsAbstract()) {
        mt.IsAbstract = true;
      }

      // Create data properties declared on this type (not base types)
      mt.DataProperties = et.GetProperties()
        .Where(p => p.DeclaringEntityType == et)
        .Select(p => CreateDataProperty(p)).ToList();

      // EF returns parent's key with the complex type - we need to remove this.
      if (mt.IsComplexType) {
        mt.DataProperties = mt.DataProperties.Where(dp => dp.IsPartOfKey == null).ToList();
      }

      if (!mt.IsComplexType) {
        mt.AutoGeneratedKeyType = mt.DataProperties.Any(dp => dp.IsIdentityColumn) ? AutoGeneratedKeyType.Identity : AutoGeneratedKeyType.None;
      }

      // Handle complex properties
      // for now this only complex types ( 'owned types' in EF parlance are eager loaded)
      var ownedNavigations = et.GetNavigations()
        .Where(p => p.DeclaringEntityType == et)
        .Where(n => n.TargetEntityType.IsOwned());
      ownedNavigations.ToList().ForEach(n => {
        var complexType = n.TargetEntityType.ClrType;
        var dp = new MetaDataProperty();
        dp.NameOnServer = n.Name;
        dp.IsNullable = false;
        dp.IsPartOfKey = false;
        dp.ComplexTypeName = NormalizeTypeName(complexType);
        mt.DataProperties.Add(dp);
      });

      mt.NavigationProperties = et.GetNavigations()
        .Where(p => p.DeclaringEntityType == et)
        .Where(n => !n.TargetEntityType.IsOwned()).Select(p => CreateNavProperty(p)).ToList();

      return mt;
    }

    private MetaDataProperty CreateDataProperty(IProperty p) {
      var dp = new MetaDataProperty();

      dp.NameOnServer = p.Name;
      dp.IsNullable = p.IsNullable;
      dp.IsPartOfKey = p.IsPrimaryKey() ? true : (bool?)null;
      dp.IsIdentityColumn = p.IsPrimaryKey() && p.ValueGenerated == ValueGenerated.OnAdd;
      dp.MaxLength = p.GetMaxLength();
      if (IsEnum(p.ClrType)) {
        dp.DataType = NormalizeDataTypeName(typeof(int));
        dp.EnumType = NormalizeTypeName(TypeFns.GetNonNullableType(p.ClrType));
      } else {
        dp.DataType = NormalizeDataTypeName(p.ClrType);
      }
      dp.ConcurrencyMode = p.IsConcurrencyToken ? "Fixed" : null;
      var dfa = p.GetAnnotations().Where(a => a.Name == "DefaultValue").FirstOrDefault();
      if (dfa != null) {
        dp.DefaultValue = dfa.Value;
      } else if (!p.IsNullable) {
        // TODO: this should really be done on the client.
        if (p.ClrType == typeof(TimeSpan)) {
          dp.DefaultValue = "PT0S";
        }
        // dp.DefaultValue = // get default value for p.ClrType datatype
      }
      dp.AddValidators(p.ClrType);
      return dp;
    }

    private MetaNavProperty CreateNavProperty(INavigation p) {
      var np = new MetaNavProperty();
      np.NameOnServer = p.Name;
      np.EntityTypeName = NormalizeTypeName(p.TargetEntityType.ClrType);
      np.IsScalar = !p.IsCollection;
      // FK_<dependent type name>_<principal type name>_<foreign key property name>
      np.AssociationName = BuildAssocName(p);
      if (p.IsOnDependent) {
        np.AssociationName = BuildAssocName(p);
        np.ForeignKeyNamesOnServer = p.ForeignKey.Properties.Select(fkp => fkp.Name).ToList();
        if (!p.ForeignKey.PrincipalKey.IsPrimaryKey()) {
          // if FK does not relate to PK of other entity, then identify the foreign property
          np.InvForeignKeyNamesOnServer = p.ForeignKey.PrincipalKey.Properties.Select(fkp => fkp.Name).ToList();
        }
      } else {
        var invP = p.Inverse;
        string assocName;
        if (invP == null) {
          assocName = "Inv_" + BuildAssocName(p);
        } else {
          assocName = BuildAssocName(invP);
        }
        np.AssociationName = assocName;
        np.InvForeignKeyNamesOnServer = p.ForeignKey.Properties.Select(fkp => fkp.Name).ToList();
      }

      return np;
    }

    private string BuildAssocName(INavigation prop) {
      var assocName = prop.DeclaringEntityType.Name + "_" + prop.TargetEntityType.Name + "_" + prop.Name;
      return assocName;
    }

    private string NormalizeTypeName(Type type) {
      return type.Name + ":#" + type.Namespace;
    }

    private string NormalizeDataTypeName(Type type) {
      type = TypeFns.GetNonNullableType(type);
      var result = type.ToString().Replace("System.", "");
      if (result == "Byte[]") {
        return "Binary";
      } else {
        return result;
      }
    }

  }

#if !NET6_0_OR_GREATER
  class MetaTypeEqualityComparer : IEqualityComparer<MetaType> {
    public bool Equals(MetaType x, MetaType y) {
      return x.ShortName == y.ShortName; 
    }

    public int GetHashCode([DisallowNull] MetaType obj) {
      return obj.ShortName.GetHashCode(); 
    }
  }
#endif
}
